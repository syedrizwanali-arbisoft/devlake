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

using System.Net;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public sealed class DevLakeClientTests : IDisposable
{
    private const string CorrelationId = "0b8f3c52-1111-4222-8333-444455556666";
    private const string Email = "jane.doe@arbisoft.com";

    private static readonly StatsQuery LastDay = StatsQuery.ForTimeFrame("24h");

    private ExternalApiHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
    }

    private async Task<T> UseAsync<T>(Func<IDevLakeClient, Task<T>> call)
    {
        await using var scope = _host.Scope();
        return await call(scope.ServiceProvider.GetRequiredService<IDevLakeClient>());
    }

    private void Restart(Dictionary<string, string?> settings)
    {
        _host.Dispose();
        _host = new ExternalApiHost(settings);
    }

    [Fact]
    public async Task IsTokenValid_SendsTheApiTokenAsABearer_AndTheCorrelationId()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "true");

        Assert.True(await UseAsync(client => client.IsTokenValidAsync(CorrelationId, CancellationToken.None)));

        var request = Assert.Single(_host.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://devlake.test/rest/is_valid", request.AbsoluteUri);
        Assert.Equal($"Bearer {ExternalApiHost.ApiToken}", request.Authorization);
        Assert.Equal(CorrelationId, request.CorrelationId);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "false")]
    [InlineData(HttpStatusCode.OK, "\"true\"")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    public async Task IsTokenValid_IsFalse_WhenDevLakeRefusesTheToken_OrDoesNotSayTrue(HttpStatusCode status, string body)
    {
        _host.Handler.Respond(status, body);

        Assert.False(await UseAsync(client => client.IsTokenValidAsync(CorrelationId, CancellationToken.None)));
        Assert.Single(_host.Handler.Requests);
    }

    [Fact]
    public async Task IsTokenValid_WhenDevLakeKeepsFailing_ThrowsUnavailable_NotFalse()
    {
        _host.Handler.Respond(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.IsTokenValidAsync(CorrelationId, CancellationToken.None)));

        Assert.Equal("DevLake", exception.SystemName);
        Assert.Equal(503, exception.StatusCode);
    }

    [Theory]
    [InlineData(DevLakeStatsSource.Jira, "jira")]
    [InlineData(DevLakeStatsSource.Github, "github")]
    [InlineData(DevLakeStatsSource.AiUsage, "ai_usage")]
    [InlineData(DevLakeStatsSource.AiCost, "ai_cost")]
    public async Task GetStats_CallsTheEndpointOfEachSource(DevLakeStatsSource source, string segment)
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{\"commits\":3}");

        var stats = await UseAsync(client => client.GetStatsAsync(source, Email, LastDay, CorrelationId, CancellationToken.None));

        Assert.Equal(3, stats.GetProperty("commits").GetInt32());
        var request = Assert.Single(_host.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"https://devlake.test/rest/stats/jane.doe%40arbisoft.com/{segment}?tf=24h", request.AbsoluteUri);
        Assert.Equal($"Bearer {ExternalApiHost.ApiToken}", request.Authorization);
        Assert.Equal(CorrelationId, request.CorrelationId);
    }

    [Fact]
    public async Task GetStats_ForARange_SendsFromAndToInUtc()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{}");
        var range = StatsQuery.ForRange(new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.FromHours(5)), new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));

        await UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Github, Email, range, CorrelationId, CancellationToken.None));

        Assert.EndsWith("/github?from=2026-11-01T00%3A00%3A00Z&to=2026-11-08T00%3A00%3A00Z", Assert.Single(_host.Handler.Requests).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStats_EscapesAnEmailSoItCannotChangeThePath()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{}");

        await UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, "a/b?c=d@arbisoft.com", LastDay, CorrelationId, CancellationToken.None));

        Assert.Contains("/stats/a%2Fb%3Fc%3Dd%40arbisoft.com/jira?tf=24h", Assert.Single(_host.Handler.Requests).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStats_ForAnEmailWithNoData_ReturnsWhatDevLakeSent_NotAnError()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{\"items\":[]}");

        var stats = await UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None));

        Assert.Equal(0, stats.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetStats_WhenDevLakeRejectsTheToken_ThrowsAuthentication_WithoutRetrying(HttpStatusCode status)
    {
        _host.Handler.Respond(status);

        await Assert.ThrowsAsync<ExternalAuthenticationException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Single(_host.Handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task GetStats_WhenDevLakeRefusesTheRequest_ThrowsRejected_WithoutRetrying(HttpStatusCode status)
    {
        _host.Handler.Respond(status, "{\"error\":\"nope\"}");

        var exception = await Assert.ThrowsAsync<ExternalApiRejectedException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal((int)status, exception.StatusCode);
        Assert.Single(_host.Handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task GetStats_RetriesTransientFailures_ThenReportsUnavailable(HttpStatusCode status)
    {
        _host.Handler.Respond(status);

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal(4, _host.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetStats_ARetryThatSucceeds_ReturnsTheStats()
    {
        _host.Handler.Respond(HttpStatusCode.BadGateway).Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK, "{\"ok\":true}");

        var stats = await UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None));

        Assert.True(stats.GetProperty("ok").GetBoolean());
        Assert.Equal(3, _host.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetStats_RetriesAConnectionError_ThenReportsUnavailable()
    {
        _host.Handler.Fail(new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal(4, _host.Handler.Requests.Count);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task GetStats_ConfigurationControlsTheNumberOfRetries()
    {
        Restart(new Dictionary<string, string?> { ["Resilience:MaxRetries"] = "1" });
        _host.Handler.Respond(HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal(2, _host.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetStats_ASlowDevLake_TimesOutAndIsReportedUnavailable()
    {
        Restart(new Dictionary<string, string?> { ["DevLake:TimeoutSeconds"] = "1", ["Resilience:MaxRetries"] = "0" });
        _host.Handler.RespondSlowly(TimeSpan.FromSeconds(30));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetStats_AnAnswerThatIsNotJson_IsAResponseError()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "<html>maintenance</html>", "text/html");

        await Assert.ThrowsAsync<ExternalApiResponseException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));
    }

    [Fact]
    public async Task GetStats_WhenTheCallerCancels_ThrowsCancellation_NotUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        _host.Handler.Then(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, cancellation.Token)));
    }

    [Fact]
    public async Task NoMoreThanTheConfiguredNumberOfRequests_AreInFlightAtOnce()
    {
        Restart(new Dictionary<string, string?> { ["DevLake:MaxConcurrentRequests"] = "2" });
        _host.Handler.Then(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        await using var scope = _host.Scope();
        var client = scope.ServiceProvider.GetRequiredService<IDevLakeClient>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal(8, _host.Handler.Requests.Count);
        Assert.Equal(2, _host.Handler.PeakConcurrency);
    }

    [Fact]
    public async Task TheDefaultConcurrencyCap_IsEight()
    {
        _host.Handler.Then(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        await using var scope = _host.Scope();
        var client = scope.ServiceProvider.GetRequiredService<IDevLakeClient>();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal(8, _host.Handler.PeakConcurrency);
    }

    [Fact]
    public async Task AFailure_IsLoggedWithTheCorrelationId_ButNeverTheEmailOrTheToken()
    {
        _host.Handler.Respond(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Contains(CorrelationId, _host.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain("jane.doe", _host.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain(ExternalApiHost.ApiToken, _host.Logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOpenCircuit_StopsCallsFromReachingDevLake_ThenProbesAgainAfterTheBreak()
    {
        Restart(new Dictionary<string, string?>
        {
            ["Resilience:MaxRetries"] = "0",
            ["Resilience:BreakerMinimumThroughput"] = "2",
            ["Resilience:BreakerFailureRatio"] = "0.5",
            ["Resilience:BreakerBreakSeconds"] = "1"
        });
        _host.Handler.Respond(HttpStatusCode.InternalServerError).Respond(HttpStatusCode.InternalServerError).Respond(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));
        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));
        var reached = _host.Handler.Requests.Count;

        var blocked = await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None)));

        Assert.Equal(reached, _host.Handler.Requests.Count);
        Assert.Contains("paused", blocked.Message, StringComparison.Ordinal);

        await Task.Delay(TimeSpan.FromMilliseconds(1300));
        var recovered = await UseAsync(client => client.GetStatsAsync(DevLakeStatsSource.Jira, Email, LastDay, CorrelationId, CancellationToken.None));
        Assert.Equal(JsonValueKindObject, recovered.ValueKind.ToString());
    }

    private const string JsonValueKindObject = "Object";

    [Fact]
    public void AStatsQuery_RejectsAnEmptyTimeFrame_AndABackwardsRange()
    {
        Assert.Throws<ArgumentException>(() => StatsQuery.ForTimeFrame(""));
        Assert.Throws<ArgumentException>(() => StatsQuery.ForRange(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
