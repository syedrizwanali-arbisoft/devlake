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
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.API.Middleware;

/// <summary>
/// The only place error responses for unexpected failures are produced. Queue problems get their own status so a client knows to retry;
/// everything else is a 500. The response carries a stable code and the correlation ID, never exception detail.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public const string ErrorCode = "internal_error";
    public const string QueueFullCode = "queue_full";
    public const string QueueUnavailableCode = "queue_unavailable";
    public const string UpstreamUnavailableCode = "upstream_unavailable";
    public const string UpstreamCredentialsCode = "upstream_credentials";
    public const string UpstreamRejectedCode = "upstream_rejected";
    public const string UpstreamBadResponseCode = "upstream_bad_response";

    private const int QueueRetryAfterSeconds = 60;
    private const string UnknownRoute = "(unknown route)";

    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = Classify(exception);
        Log(exception, httpContext, status);

        httpContext.Response.StatusCode = status;
        if (status is StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = QueueRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } };
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }

    private static (int Status, string Title, string Code) Classify(Exception exception)
    {
        return exception switch
        {
            QueueFullException => (StatusCodes.Status429TooManyRequests, "The request queue is full. Try again shortly.", QueueFullCode),
            QueueUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The request queue is temporarily unavailable. Try again shortly.", QueueUnavailableCode),
            ExternalApiUnavailableException => (StatusCodes.Status503ServiceUnavailable, "A system this service depends on is temporarily unavailable. Try again shortly.", UpstreamUnavailableCode),
            ExternalAuthenticationException => (StatusCodes.Status502BadGateway, "A system this service depends on rejected its credentials.", UpstreamCredentialsCode),
            ExternalApiRejectedException => (StatusCodes.Status502BadGateway, "A system this service depends on refused the request.", UpstreamRejectedCode),
            ExternalApiResponseException => (StatusCodes.Status502BadGateway, "A system this service depends on sent an answer that could not be used.", UpstreamBadResponseCode),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", ErrorCode)
        };
    }

    private void Log(Exception exception, HttpContext httpContext, int status)
    {
        var route = RouteOf(httpContext);
        if (status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Route}", httpContext.Request.Method, route);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Route} was answered {Status} because of a problem outside this service", httpContext.Request.Method, route, status);
        }
    }

    /// <summary>
    /// The route pattern, such as <c>.../engineers/{email}/stats</c>, never the request path: the path can hold a raw email address,
    /// which must not reach a log (SEC-P1).
    /// </summary>
    private static string RouteOf(HttpContext httpContext)
    {
        return (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? UnknownRoute;
    }
}
