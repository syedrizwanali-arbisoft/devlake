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
using System.Threading.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.API.RateLimiting;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public int StatsPerMinute { get; set; } = 100;
    public int BatchStatsPerMinute { get; set; } = 20;
    public int PollPerMinute { get; set; } = 100;
}

/// <summary>Per-client limits agreed with Sophia. Each client, identified by its token's client_id, gets its own window.</summary>
public static class RateLimitPolicies
{
    public const string Stats = "stats";
    public const string BatchStats = "batch-stats";
    public const string Poll = "poll";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { status = "429", error = "Rate limit exceeded. Try again later." }, cancellationToken);
            };

            AddPerClientPolicy(options, Stats, limits => limits.StatsPerMinute);
            AddPerClientPolicy(options, BatchStats, limits => limits.BatchStatsPerMinute);
            AddPerClientPolicy(options, Poll, limits => limits.PollPerMinute);
        });
        return services;
    }

    private static void AddPerClientPolicy(
        Microsoft.AspNetCore.RateLimiting.RateLimiterOptions options, string name, Func<RateLimitOptions, int> permitLimit)
    {
        options.AddPolicy(name, context =>
        {
            var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
            var clientId = context.User.FindFirst(TokenClaimNames.ClientId)?.Value ?? string.Empty;
            return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit(limits),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        });
    }
}
