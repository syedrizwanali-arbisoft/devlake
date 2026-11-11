// Licensed to the Apache Software Foundation (ASF) under one or more
// contributor license agreements.  See the NOTICE file distributed with
// this work for additional information regarding copyright ownership.
// The ASF licenses this file to You under the Apache License, Version 2.0
// (the "License"); you may not use this file except in compliance with
// the License.  You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.API.RateLimiting;

/// <summary>
/// Per-client rate limits at the edge (API-9). Each client has its own allowance, so one busy client cannot use up another's. A client over a
/// limit gets 429 with Retry-After.
/// </summary>
public static class RateLimitPolicies
{
    public const string SingleStats = "stats-single";
    public const string BatchStats = "stats-batch";
    public const string RequestPoll = "requests-poll";
    public const string ErrorCode = "rate_limited";

    private const int SegmentsPerWindow = 6;
    private const int FallbackRetryAfterSeconds = 60;
    private const string AnonymousPartition = "anonymous";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>(Configure);
        return services;
    }

    private static void Configure(RateLimiterOptions options, IOptions<RateLimitOptions> limits)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(SingleStats, context => PerClient(context, limits.Value.StatsPerMinute));
        options.AddPolicy(BatchStats, context => PerClient(context, limits.Value.BatchPerMinute));
        options.AddPolicy(RequestPoll, context => PerClient(context, limits.Value.PollPerMinute));
        options.OnRejected = WriteRejectionAsync;
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitsPerMinute)
    {
        var client = context.User.FindFirstValue(TokenClaims.Client) ?? context.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition;
        return RateLimitPartition.GetSlidingWindowLimiter(client, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = Window,
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0
        });
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? (int)Math.Ceiling(delay.TotalSeconds) : FallbackRetryAfterSeconds;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Extensions = { ["code"] = ErrorCode }
        };
        await http.RequestServices.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }
}
