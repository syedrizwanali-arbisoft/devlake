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

using Arbisoft.DevLake.IntegrationService.API.Middleware;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly IProblemDetailsService _problemDetails = Substitute.For<IProblemDetailsService>();
    private readonly GlobalExceptionHandler _handler;
    private ProblemDetailsContext? _written;

    public GlobalExceptionHandlerTests()
    {
        _problemDetails.TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => _written = context)).Returns(ValueTask.FromResult(true));
        _handler = new GlobalExceptionHandler(_problemDetails, NullLogger<GlobalExceptionHandler>.Instance);
    }

    private async Task<DefaultHttpContext> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        Assert.True(await _handler.TryHandleAsync(context, exception, CancellationToken.None));
        return context;
    }

    [Fact]
    public async Task AFullQueue_Is429WithRetryAfter_AndAStableCode()
    {
        var context = await HandleAsync(new QueueFullException("internal detail about the broker"));

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("queue_full", _written!.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task AnUnreachableBroker_Is503WithRetryAfter()
    {
        var context = await HandleAsync(new QueueUnavailableException("amqp://secret-host:5672 refused"));

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("queue_unavailable", _written!.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task AnUnavailableUpstreamSystem_Is503WithRetryAfter()
    {
        var context = await HandleAsync(new ExternalApiUnavailableException("Workstream", "Workstream at 10.1.2.3 refused the connection"));

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("upstream_unavailable", _written!.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData(typeof(ExternalAuthenticationException), "upstream_credentials")]
    [InlineData(typeof(ExternalApiRejectedException), "upstream_rejected")]
    [InlineData(typeof(ExternalApiResponseException), "upstream_bad_response")]
    public async Task AnUpstreamSystemThatRefusedOrAnsweredBadly_Is502_WithAStableCode_AndNoRetryAfter(Type type, string code)
    {
        var exception = type == typeof(ExternalApiRejectedException)
            ? (Exception)new ExternalApiRejectedException("Workstream", "SECRET-DETAIL", 400)
            : type == typeof(ExternalAuthenticationException)
                ? new ExternalAuthenticationException("Workstream", "SECRET-DETAIL", 401)
                : new ExternalApiResponseException("Workstream", "SECRET-DETAIL");

        var context = await HandleAsync(exception);

        Assert.Equal(502, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(code, _written!.ProblemDetails.Extensions["code"]);
        Assert.DoesNotContain("SECRET-DETAIL", _written.ProblemDetails.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnythingElse_Is500_WithoutRetryAfter()
    {
        var context = await HandleAsync(new InvalidOperationException("password=hunter2"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal("internal_error", _written!.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task TheLog_NamesTheRoutePattern_NeverTheRequestPathThatCanHoldAnEmail()
    {
        var logger = new CapturingLogger();
        var handler = new GlobalExceptionHandler(_problemDetails, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/engineers/jane.doe@arbisoft.com/stats";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/v1/engineers/{email}/stats"), 0, null, "stats"));

        foreach (var exception in new Exception[] { new InvalidOperationException("boom"), new QueueFullException("full"), new QueueUnavailableException("down") })
        {
            await handler.TryHandleAsync(context, exception, CancellationToken.None);
        }

        Assert.Equal(3, logger.Messages.Count);
        Assert.All(logger.Messages, message =>
        {
            Assert.DoesNotContain("jane.doe", message, StringComparison.Ordinal);
            Assert.Contains("api/v1/engineers/{email}/stats", message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task TheLog_SaysSoWhenTheRouteIsNotKnown()
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = "/somewhere/jane@arbisoft.com";

        await new GlobalExceptionHandler(_problemDetails, logger).TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.DoesNotContain("jane", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<GlobalExceptionHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    [Theory]
    [InlineData(typeof(QueueFullException))]
    [InlineData(typeof(QueueUnavailableException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task NoResponseRepeatsTheExceptionMessage(Type exceptionType)
    {
        await HandleAsync((Exception)Activator.CreateInstance(exceptionType, "SECRET-DETAIL", null)!);

        Assert.DoesNotContain("SECRET-DETAIL", _written!.ProblemDetails.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-DETAIL", _written.ProblemDetails.Detail ?? string.Empty, StringComparison.Ordinal);
    }
}
