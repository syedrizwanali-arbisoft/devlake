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
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

/// <summary>
/// The whole pipeline: a client's request goes in through the API, onto RabbitMQ, is picked up by the processing job, collected from a stubbed DevLake,
/// delivered to a stubbed Sophia, and shows up through the poll API. MySQL, RabbitMQ, the HTTP pipeline and the job are all real.
/// </summary>
[Collection(MySqlTestGroup.Name)]
public sealed class ProcessingEndToEndTests : ApiTestBase
{
    private const string Single = RequestEventTypes.EngineerStats;
    private const string Batch = RequestEventTypes.EngineerStatsBatch;

    private readonly UpstreamStub _upstream;

    public ProcessingEndToEndTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string>
        {
            ["Processing:Enabled"] = "true",
            ["Processing:IntervalSeconds"] = "1",
            ["Processing:DeliveryRetryDelaySeconds"] = "1",
            ["Resilience:RetryBaseDelayMilliseconds"] = "1",
            ["RateLimits:PollPerMinute"] = "100000"
        })
    {
        _upstream = new UpstreamStub(Upstream);
    }

    /// <summary>A client with callbacks for the given event types, and every other client's outbound config turned off.</summary>
    private async Task<(SeededClient Client, string Token)> IsolatedClientAsync(params string[] eventTypes)
    {
        var result = await ClientWithCallbacksAsync(eventTypes);
        await Seed.IsolateAsync(result.Client);
        return result;
    }

    private async Task<string> PollAsync(string token, string correlationId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{correlationId}", token);
        return response.Json.GetProperty("status").GetString()!;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int seconds = 20)
    {
        await RabbitMqHarness.WaitUntilAsync(condition, seconds);
    }

    private async Task WaitForStatusAsync(string token, string correlationId, string expected)
    {
        await WaitForAsync(async () => await PollAsync(token, correlationId) == expected);
    }

    [BrokersFact]
    public async Task ASingleRequest_IsCollectedFromDevLake_DeliveredToSophia_AndReportedSuccess()
    {
        var (client, token) = await IsolatedClientAsync(Single);
        var jane = $"jane-{Guid.NewGuid():N}@arbisoft.com";
        var alias = $"jane.d-{Guid.NewGuid():N}@gmail.com";
        await Seed.SeedEmailMappingAsync(jane, alias);

        var request = await GetStatsAsync(token, jane, "24h");
        await WaitForStatusAsync(token, request.Correlation, "Success");

        var callback = Assert.Single(_upstream.Callbacks);
        Assert.Equal(HttpMethod.Post, callback.Method);
        Assert.Equal("https://sophia.test/api/v1/devlake/callback/", callback.AbsoluteUri);
        Assert.Equal("Bearer tok-1", callback.Authorization);
        Assert.Equal(request.Correlation, callback.CorrelationId);

        var body = JsonDocument.Parse(callback.Content!).RootElement;
        Assert.Equal(request.Correlation, body.GetProperty("correlation_id").GetString());
        Assert.Equal("24h", body.GetProperty("time_frame").GetString());
        var engineer = Assert.Single(body.GetProperty("engineers").EnumerateArray());
        Assert.Equal(jane, engineer.GetProperty("email").GetString());
        Assert.Equal(alias, Assert.Single(engineer.GetProperty("mapped_emails").EnumerateArray()).GetString());
        foreach (var (property, source) in new[] { ("jira", "jira"), ("github", "github"), ("ai_usage", "ai_usage"), ("ai_cost", "ai_cost") })
        {
            var stats = engineer.GetProperty(property);
            Assert.Equal(source, stats.GetProperty(jane).GetProperty("source").GetString());
            Assert.Equal(source, stats.GetProperty(alias).GetProperty("source").GetString());
        }

        var statsCalls = _upstream.DevLakeStatsRequests.ToList();
        Assert.Equal(8, statsCalls.Count);
        Assert.All(statsCalls, call =>
        {
            Assert.Equal("Bearer token", call.Authorization);
            Assert.Equal(request.Correlation, call.CorrelationId);
            Assert.EndsWith("?tf=24h", call.AbsoluteUri, StringComparison.Ordinal);
        });

        var log = (await Seed.LogAsync(request.Correlation))!;
        Assert.Equal(RequestStatus.Success, log.Status);
        var attempt = Assert.Single(await Seed.AttemptsAsync(log.Id));
        Assert.Equal("Delivered", attempt.AttemptStatus);
        Assert.Equal("RequestProcessor", attempt.AttemptBy);
        Assert.Null(attempt.FailureError);
        Assert.Contains("received", attempt.ResponseJson, StringComparison.Ordinal);
        Assert.Equal(0, await Seed.QueueDepthAsync());
        Assert.Single(await Seed.LogsForAsync(client));
    }

    [BrokersFact]
    public async Task ARangeRequest_AsksDevLakeForTheRange_AndDeliversFromAndTo()
    {
        var (_, token) = await IsolatedClientAsync(Single);

        var request = await SendAsync(HttpMethod.Get, $"/api/v1/engineers/{Encode("jane@arbisoft.com")}/stats?from={Uri.EscapeDataString("2026-11-01T00:00:00Z")}&to={Uri.EscapeDataString("2026-11-08T00:00:00Z")}", token);
        await WaitForStatusAsync(token, request.Correlation, "Success");

        Assert.All(_upstream.DevLakeStatsRequests, call => Assert.EndsWith("?from=2026-11-01T00%3A00%3A00Z&to=2026-11-08T00%3A00%3A00Z", call.AbsoluteUri, StringComparison.Ordinal));
        var body = JsonDocument.Parse(Assert.Single(_upstream.Callbacks).Content!).RootElement;
        Assert.Equal("2026-11-01T00:00:00Z", body.GetProperty("from").GetString());
        Assert.Equal("2026-11-08T00:00:00Z", body.GetProperty("to").GetString());
        Assert.False(body.TryGetProperty("time_frame", out _));
    }

    [BrokersFact]
    public async Task ABatch_IsProcessedForEveryEmail_AndDeliveredOnceWithOneEntryEach()
    {
        var (_, token) = await IsolatedClientAsync(Batch);
        var a = $"a-{Guid.NewGuid():N}@arbisoft.com";
        var b = $"b-{Guid.NewGuid():N}@arbisoft.com";
        await Seed.SeedEmailMappingAsync(b, $"bee-{Guid.NewGuid():N}@gmail.com");

        var request = await PostBatchAsync(token, new { emails = new[] { a, b }, timeFrame = "1w" });
        await WaitForStatusAsync(token, request.Correlation, "Success");

        var body = JsonDocument.Parse(Assert.Single(_upstream.Callbacks).Content!).RootElement;
        Assert.Equal([a, b], body.GetProperty("engineers").EnumerateArray().Select(e => e.GetProperty("email").GetString()));
        Assert.Equal(12, _upstream.DevLakeStatsRequests.Count());
    }

    [BrokersFact]
    public async Task WhenSophiaKeepsFailing_EveryAttemptIsRecorded_AndTheRequestEndsFailed()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        _upstream.ScriptCallbacks(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);

        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForStatusAsync(token, request.Correlation, "Failure");

        Assert.Equal(3, _upstream.Callbacks.Count());
        var log = (await Seed.LogAsync(request.Correlation))!;
        var attempts = await Seed.AttemptsAsync(log.Id);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, attempt =>
        {
            Assert.Equal("DeliveryFailed", attempt.AttemptStatus);
            Assert.Equal("HTTP 500", attempt.FailureError);
            Assert.Contains("sophia says no", attempt.ResponseJson, StringComparison.Ordinal);
        });
        Assert.Equal(0, await Seed.QueueDepthAsync());
    }

    [BrokersFact]
    public async Task ADeliveryThatFailsOnceThenWorks_EndsSuccess_WithBothAttemptsRecorded()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        _upstream.ScriptCallbacks(HttpStatusCode.BadGateway);

        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForStatusAsync(token, request.Correlation, "Success");

        var log = (await Seed.LogAsync(request.Correlation))!;
        Assert.Equal(["DeliveryFailed", "Delivered"], (await Seed.AttemptsAsync(log.Id)).Select(a => a.AttemptStatus));
        Assert.Equal(2, _upstream.Callbacks.Count());
        Assert.Single(_upstream.SophiaTokenRequests);
    }

    [BrokersFact]
    public async Task TheNumberOfDeliveryAttempts_ComesFromTheClientsConfig()
    {
        var client = await Seed.SeedClientAsync();
        await Seed.SeedSophiaAsync(client, maxAttempts: 1, Single);
        await Seed.IsolateAsync(client);
        var token = await ApiSeed.RequestTokenAsync(Http, client);
        _upstream.ScriptCallbacks(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);

        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForStatusAsync(token, request.Correlation, "Failure");

        Assert.Single(_upstream.Callbacks);
    }

    [BrokersFact]
    public async Task WhenDevLakeRejectsOurToken_RequestsStayQueuedUntouched_ThenCompleteOnceItIsFixed()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        _upstream.DevLakeTokenValid = false;

        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForAsync(async () => _upstream.DevLakeTokenChecks.Count() >= 2);

        Assert.Equal("Queued", await PollAsync(token, request.Correlation));
        Assert.Equal(1, await Seed.QueueDepthAsync());
        Assert.Empty(_upstream.DevLakeStatsRequests);
        Assert.Empty(_upstream.Callbacks);
        var message = (await Seed.LogAsync(request.Correlation))!;
        Assert.Empty(await Seed.AttemptsAsync(message.Id));

        _upstream.DevLakeTokenValid = true;
        await WaitForStatusAsync(token, request.Correlation, "Success");
        Assert.Equal(0, await Seed.QueueDepthAsync());
    }

    [BrokersFact]
    public async Task WhenSophiaRefusesOurCredentials_RequestsStayQueued_ThenCompleteOnceItIsFixed()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        _upstream.SophiaTokenStatus = HttpStatusCode.Unauthorized;

        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForAsync(async () => _upstream.SophiaTokenRequests.Count() >= 2);

        Assert.Equal("Queued", await PollAsync(token, request.Correlation));
        Assert.Equal(1, await Seed.QueueDepthAsync());
        Assert.Empty(_upstream.DevLakeStatsRequests);

        _upstream.SophiaTokenStatus = HttpStatusCode.OK;
        await WaitForStatusAsync(token, request.Correlation, "Success");
    }

    [BrokersFact]
    public async Task ACallbackRemovedWhileTheRequestWaits_RevokesIt_WithoutCallingDevLake()
    {
        var (client, token) = await IsolatedClientAsync(Single);
        _upstream.DevLakeTokenValid = false;
        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForAsync(async () => _upstream.DevLakeTokenChecks.Any());

        await Seed.DeactivateCallbackUrlAsync(client, Single);
        _upstream.DevLakeTokenValid = true;
        await WaitForStatusAsync(token, request.Correlation, "Revoked");

        Assert.Empty(_upstream.DevLakeStatsRequests);
        Assert.Empty(_upstream.Callbacks);
        Assert.Equal(RequestStatus.RevokedNoCallbackUrl, (await Seed.LogAsync(request.Correlation))!.Status);
        Assert.Equal(0, await Seed.QueueDepthAsync());
    }

    [BrokersFact]
    public async Task WhenDevLakeRefusesTheRequest_ItFailsOnItsOwn_WithTheReasonRecorded_AndNothingIsDelivered()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        var jane = $"jane-{Guid.NewGuid():N}@arbisoft.com";
        var alias = $"jane.d-{Guid.NewGuid():N}@gmail.com";
        await Seed.SeedEmailMappingAsync(jane, alias);
        _upstream.DevLakeStats = email => email == alias ? HttpStatusCode.NotFound : HttpStatusCode.OK;

        var request = await GetStatsAsync(token, jane);
        await WaitForStatusAsync(token, request.Correlation, "Failure");

        Assert.Empty(_upstream.Callbacks);
        var attempt = Assert.Single(await Seed.AttemptsAsync((await Seed.LogAsync(request.Correlation))!.Id));
        Assert.Equal("FetchFailed", attempt.AttemptStatus);
        Assert.Contains("DevLake", attempt.FailureError, StringComparison.Ordinal);
        Assert.Equal(0, await Seed.QueueDepthAsync());
    }

    [BrokersFact]
    public async Task OneFailingRequest_DoesNotStopTheOthers()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        _upstream.DevLakeStats = email => email.StartsWith("bad", StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.OK;

        var bad = await GetStatsAsync(token, "bad@arbisoft.com");
        var good = await Task.WhenAll(Enumerable.Range(1, 4).Select(i => GetStatsAsync(token, $"good{i}@arbisoft.com")));
        await WaitForStatusAsync(token, bad.Correlation, "Failure");
        foreach (var request in good)
        {
            await WaitForStatusAsync(token, request.Correlation, "Success");
        }

        Assert.Equal(4, _upstream.Callbacks.Count());
        Assert.Equal(0, await Seed.QueueDepthAsync());
    }

    [BrokersFact]
    public async Task ManyRequestsAtOnce_AreAllDelivered_ExactlyOnceEach()
    {
        var (_, token) = await IsolatedClientAsync(Single);

        var requests = await Task.WhenAll(Enumerable.Range(1, 12).Select(i => GetStatsAsync(token, $"engineer{i}@arbisoft.com")));
        foreach (var request in requests)
        {
            await WaitForStatusAsync(token, request.Correlation, "Success");
        }

        var delivered = _upstream.Callbacks.Select(c => JsonDocument.Parse(c.Content!).RootElement.GetProperty("correlation_id").GetString()).ToList();
        Assert.Equal(12, delivered.Count);
        Assert.Equal(requests.Select(r => r.Correlation).Order(), delivered.Order());
    }

    [BrokersFact]
    public async Task ARepeatDeliveryOfAFinishedRequest_IsAcknowledged_WithoutDeliveringAgain()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        var request = await GetStatsAsync(token, "jane@arbisoft.com");
        await WaitForStatusAsync(token, request.Correlation, "Success");
        var body = JsonSerializer.Serialize(new { correlation_id = request.Correlation, event_type = Single, emails = new[] { "jane@arbisoft.com" }, time_frame = "24h", requested_on = DateTimeOffset.UtcNow });

        await RabbitMqHarness.PublishRawAsync(Factory.QueueName, request.Correlation, Single, body);
        await WaitForAsync(async () => await Seed.QueueDepthAsync() == 0);
        await Task.Delay(1500);

        Assert.Single(_upstream.Callbacks);
        Assert.Equal(4, _upstream.DevLakeStatsRequests.Count());
    }

    [BrokersFact]
    public async Task AMessageWhoseLogRowDoesNotExist_StaysQueued_NotLostAndNotProcessed()
    {
        var (_, _) = await IsolatedClientAsync(Single);
        var correlationId = Guid.NewGuid().ToString("D");
        var body = JsonSerializer.Serialize(new { correlation_id = correlationId, event_type = Single, emails = new[] { "ghost@arbisoft.com" }, time_frame = "24h", requested_on = DateTimeOffset.UtcNow });

        await RabbitMqHarness.PublishRawAsync(Factory.QueueName, correlationId, Single, body);
        await Task.Delay(3500);

        Assert.Equal(1, await Seed.QueueDepthAsync());
        Assert.Empty(_upstream.DevLakeStatsRequests);
        Assert.Empty(_upstream.Callbacks);
    }

    [BrokersFact]
    public async Task ARequestLeftUnfinishedPastTheQueueLifetime_IsMarkedFailed_SoPollingDoesNotSayQueuedForever()
    {
        var (client, token) = await IsolatedClientAsync(Single);
        var correlationId = await Seed.SeedRequestAsync(client, RequestStatus.Queued);
        await Seed.ExecuteAsync("UPDATE RequestLogs SET createdOn = @old WHERE correlationId = @id", new { old = DateTime.UtcNow.AddHours(-3), id = correlationId });

        await WaitForStatusAsync(token, correlationId, "Failure");

        var attempt = Assert.Single(await Seed.AttemptsAsync((await Seed.LogAsync(correlationId))!.Id));
        Assert.Equal("Expired", attempt.AttemptStatus);
        Assert.Contains("expired", attempt.FailureError, StringComparison.Ordinal);
    }

    [BrokersFact]
    public async Task WithAnEmptyQueue_TheJobDoesNotEvenCheckDevLake()
    {
        await Task.Delay(3500);

        Assert.Empty(_upstream.DevLakeTokenChecks);
        Assert.Empty(_upstream.SophiaTokenRequests);
    }

    [BrokersFact]
    public async Task EveryCallToDevLakeAndSophia_CarriesTheRequestsCorrelationId()
    {
        var (_, token) = await IsolatedClientAsync(Single);
        var request = await GetStatsAsync(token, "private.person@arbisoft.com");
        await WaitForStatusAsync(token, request.Correlation, "Success");

        Assert.All(_upstream.DevLakeStatsRequests.Concat(_upstream.Callbacks), call => Assert.Equal(request.Correlation, call.CorrelationId));
    }
}
