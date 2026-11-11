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
using System.Net;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

/// <summary>The duplicate window is a setting: with one minute, a request two minutes old is no longer a duplicate.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class ShortDuplicateWindowTests : ApiTestBase
{
    public ShortDuplicateWindowTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string> { ["Requests:DuplicateWindowMinutes"] = "1" })
    {
    }

    [BrokersFact]
    public async Task ARequestTwoMinutesOld_IsNotADuplicate_WhenTheWindowIsOneMinute()
    {
        var (_, token) = await ClientWithCallbacksAsync(RequestEventTypes.EngineerStats);
        var first = await GetStatsAsync(token, "jane@arbisoft.com");
        var immediate = await GetStatsAsync(token, "jane@arbisoft.com");
        await Seed.ExecuteAsync("UPDATE RequestLogs SET createdOn = @old WHERE correlationId = @id", new { old = DateTime.UtcNow.AddMinutes(-2), id = first.Correlation });

        var later = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.Equal(first.Correlation, immediate.Correlation);
        Assert.NotEqual(first.Correlation, later.Correlation);
    }
}

/// <summary>A longer window keeps an older request a duplicate, with no code change.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class LongDuplicateWindowTests : ApiTestBase
{
    public LongDuplicateWindowTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string> { ["Requests:DuplicateWindowMinutes"] = "60" })
    {
    }

    [BrokersFact]
    public async Task ARequestTwentyMinutesOld_IsStillADuplicate_WhenTheWindowIsAnHour()
    {
        var (_, token) = await ClientWithCallbacksAsync(RequestEventTypes.EngineerStats);
        var first = await GetStatsAsync(token, "jane@arbisoft.com");
        await Seed.ExecuteAsync("UPDATE RequestLogs SET createdOn = @old WHERE correlationId = @id", new { old = DateTime.UtcNow.AddMinutes(-20), id = first.Correlation });

        var again = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.Equal(first.Correlation, again.Correlation);
    }
}

/// <summary>Per-client rate limits, set low here so the tests stay fast. Requests with no callback URL never touch the broker.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class RateLimitTests : ApiTestBase
{
    public RateLimitTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string> { ["RateLimits:BatchPerMinute"] = "3", ["RateLimits:StatsPerMinute"] = "5" })
    {
    }

    [MySqlFact]
    public async Task TheBatchApi_AllowsItsLimit_ThenAnswers429WithRetryAfterAndAStableCode()
    {
        var (_, token) = await ClientWithCallbacksAsync();
        var body = new { emails = new[] { "a@arbisoft.com" }, timeFrame = "24h" };

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostBatchAsync(token, body)).Status);
        }

        var limited = await PostBatchAsync(token, body);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        var retryAfter = int.Parse(limited.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal(429, limited.Json.GetProperty("status").GetInt32());
        Assert.Equal("rate_limited", limited.Json.GetProperty("code").GetString());
        Assert.True(limited.Json.TryGetProperty("correlationId", out _));
    }

    [MySqlFact]
    public async Task TheSingleApi_HasItsOwnLimit_SeparateFromTheBatchApi()
    {
        var (_, token) = await ClientWithCallbacksAsync();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetStatsAsync(token, $"user{i}@arbisoft.com")).Status);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetStatsAsync(token, "user9@arbisoft.com")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostBatchAsync(token, new { emails = new[] { "a@arbisoft.com" }, timeFrame = "24h" })).Status);
    }

    [MySqlFact]
    public async Task OneClientHittingItsLimit_DoesNotAffectAnotherClient()
    {
        var (_, busy) = await ClientWithCallbacksAsync();
        var (_, quiet) = await ClientWithCallbacksAsync();
        for (var i = 0; i < 6; i++)
        {
            await GetStatsAsync(busy, $"user{i}@arbisoft.com");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetStatsAsync(busy, "more@arbisoft.com")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetStatsAsync(quiet, "jane@arbisoft.com")).Status);
    }

    [MySqlFact]
    public async Task ARequestRefusedForItsScope_DoesNotUseUpTheAllowance()
    {
        var (_, authorized) = await ClientWithCallbacksAsync();
        var client = await Seed.SeedClientAsync(scopes: ["projects:fetch"]);
        var unauthorized = await ApiSeed.RequestTokenAsync(Http, client);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await GetStatsAsync(unauthorized, "jane@arbisoft.com")).Status);
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetStatsAsync(authorized, "jane@arbisoft.com")).Status);
    }
}

/// <summary>A queue at its limit refuses new requests with 429 and Retry-After; nothing is half-recorded.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class FullQueueTests : ApiTestBase
{
    public FullQueueTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string> { ["RabbitMq:QueueMaxLength"] = "2" })
    {
    }

    [BrokersFact]
    public async Task WhenTheQueueIsFull_TheApiAnswers429_AndTheRejectedRequestLeavesNoTrace()
    {
        var (client, token) = await ClientWithCallbacksAsync(RequestEventTypes.EngineerStats);
        Assert.Equal(HttpStatusCode.OK, (await GetStatsAsync(token, "one@arbisoft.com")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetStatsAsync(token, "two@arbisoft.com")).Status);

        var rejected = await GetStatsAsync(token, "three@arbisoft.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.Status);
        Assert.Equal("60", rejected.Headers.GetValues("Retry-After").Single());
        Assert.Equal("queue_full", rejected.Json.GetProperty("code").GetString());
        Assert.DoesNotContain("amqp", rejected.Text, StringComparison.OrdinalIgnoreCase);

        var logs = await Seed.LogsForAsync(client);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal("Queued", log.Status));
        Assert.Equal(2, (await RabbitMqHarness.DrainAsync(Factory.QueueName)).Count);
    }

    [BrokersFact]
    public async Task OnceTheQueueHasRoomAgain_RequestsAreAcceptedAgain()
    {
        var (_, token) = await ClientWithCallbacksAsync(RequestEventTypes.EngineerStats);
        await GetStatsAsync(token, "one@arbisoft.com");
        await GetStatsAsync(token, "two@arbisoft.com");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetStatsAsync(token, "three@arbisoft.com")).Status);

        await RabbitMqHarness.DrainAsync(Factory.QueueName);

        await RabbitMqHarness.WaitUntilAsync(async () => (await GetStatsAsync(token, "four@arbisoft.com")).Status == HttpStatusCode.OK);
    }
}

/// <summary>With the broker unreachable the API says so with 503; requests that never need the queue still work.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class BrokerDownTests : ApiTestBase
{
    public BrokerDownTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string> { ["RabbitMq:Port"] = "1" })
    {
    }

    [MySqlFact]
    public async Task WhenTheBrokerIsUnreachable_TheApiAnswers503WithRetryAfter_AndRecordsNothing()
    {
        var (client, token) = await ClientWithCallbacksAsync(RequestEventTypes.EngineerStats);

        var response = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal("queue_unavailable", response.Json.GetProperty("code").GetString());
        Assert.Empty(await Seed.LogsForAsync(client));
    }

    [MySqlFact]
    public async Task ARequestThatNeverNeedsTheQueue_StillWorks_WhileTheBrokerIsDown()
    {
        var (_, token) = await ClientWithCallbacksAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetStatsAsync(token, "jane@arbisoft.com")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetStatsAsync(token, "not-an-email")).Status);
    }

    [MySqlFact]
    public async Task Readiness_ReportsTheBrokerAsUnavailable_ButLivenessStaysUp()
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/health/live", token: null)).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SendAsync(HttpMethod.Get, "/health/ready", token: null)).Status);
    }
}
