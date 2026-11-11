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

/// <summary>The stats intake endpoints end to end: real HTTP pipeline, MySQL and RabbitMQ.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class StatsEndpointTests : ApiTestBase
{
    private const string Single = RequestEventTypes.EngineerStats;
    private const string Batch = RequestEventTypes.EngineerStatsBatch;

    public StatsEndpointTests(MySqlDatabaseFixture database)
        : base(database)
    {
    }

    [BrokersFact]
    public async Task ASingleRequest_IsAcknowledged_Logged_AndQueued()
    {
        var (client, token) = await ClientWithCallbacksAsync(Single);

        var response = await GetStatsAsync(token, "jane.doe@arbisoft.com", "24h");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("200", response.Json.GetProperty("status").GetString());
        Assert.Equal("jane.doe@arbisoft.com", response.Json.GetProperty("email").GetString());
        Assert.Equal(string.Empty, response.Json.GetProperty("error").GetString());
        Assert.True(Guid.TryParse(response.Correlation, out _));

        var log = (await Seed.LogAsync(response.Correlation))!;
        Assert.Equal(RequestStatus.Queued, log.Status);
        Assert.Equal(client.ClientRefId, log.ClientRefId);
        Assert.Equal("engineer-stats", log.EventType);
        Assert.DoesNotContain("@", log.Url, StringComparison.Ordinal);
        Assert.NotNull(log.IdempotencyKey);

        var message = Assert.Single(await RabbitMqHarness.DrainAsync(Factory.QueueName));
        Assert.Equal(response.Correlation, message.CorrelationId);
        Assert.Equal("engineer-stats", message.EventType);
        Assert.True(message.Persistent);
        Assert.Equal(1, message.SchemaVersion);
        // MySQL stores JSON in its own canonical form, so compare the content, not the text.
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(message.Body).RootElement, JsonDocument.Parse((await Seed.LogJsonAsync(log.Id))!).RootElement));
        var body = JsonDocument.Parse(message.Body).RootElement;
        Assert.Equal("24h", body.GetProperty("time_frame").GetString());
        Assert.Equal("jane.doe@arbisoft.com", Assert.Single(body.GetProperty("emails").EnumerateArray()).GetString());
    }

    [BrokersFact]
    public async Task TheEmail_IsTrimmedAndLowerCased_BeforeItIsUsed()
    {
        var (_, token) = await ClientWithCallbacksAsync(Single);

        var response = await GetStatsAsync(token, "  Jane.Doe@ARBISOFT.com ");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("jane.doe@arbisoft.com", response.Json.GetProperty("email").GetString());
    }

    [BrokersFact]
    public async Task EveryDocumentedTimeFrame_IsAccepted()
    {
        var (_, token) = await ClientWithCallbacksAsync(Single);

        foreach (var timeFrame in new[] { "24h", "1w", "30d", "11M", "1y", "12M", "365d" })
        {
            Assert.Equal(HttpStatusCode.OK, (await GetStatsAsync(token, "jane@arbisoft.com", timeFrame)).Status);
        }

        Assert.Equal(7, (await RabbitMqHarness.DrainAsync(Factory.QueueName)).Count);
    }

    [MySqlFact]
    public async Task AnInvalidEmail_IsRejectedWith400_WithoutEchoingIt_AndNothingIsRecorded()
    {
        var (client, token) = await ClientWithCallbacksAsync(Single);

        foreach (var email in new[] { "not-an-email", "someone.secret@gmail.com", "a@arbisoft.com.evil.example", "a@sub.arbisoft.com", "bad email@arbisoft.com" })
        {
            var response = await GetStatsAsync(token, email);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            Assert.DoesNotContain("secret", response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("gmail", response.Text, StringComparison.Ordinal);
            Assert.Equal(400, response.Json.GetProperty("status").GetInt32());
            Assert.True(response.Json.TryGetProperty("correlationId", out _));
        }

        Assert.Empty(await Seed.LogsForAsync(client));
    }

    [MySqlFact]
    public async Task AnInvalidOrMissingTimeFrame_IsRejectedWith400()
    {
        var (client, token) = await ClientWithCallbacksAsync(Single);

        foreach (var timeFrame in new string?[] { "24H", "1D", "0h", "24", "h", "abc", "2y", "366d", "13M", "-1d", "1.5d", null })
        {
            var response = await GetStatsAsync(token, "jane@arbisoft.com", timeFrame);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        }

        Assert.Empty(await Seed.LogsForAsync(client));
    }

    [MySqlFact]
    public async Task WithoutTheStatsReadScope_TheCallIsForbidden_AndWithoutATokenUnauthorized()
    {
        var client = await Seed.SeedClientAsync(scopes: ["projects:fetch"]);
        var token = await ApiSeed.RequestTokenAsync(Http, client);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetStatsAsync(token, "jane@arbisoft.com")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostBatchAsync(token, new { emails = new[] { "jane@arbisoft.com" }, timeFrame = "24h" })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetStatsAsync(string.Empty, "jane@arbisoft.com")).Status);
        Assert.Empty(await Seed.LogsForAsync(client));
    }

    [BrokersFact]
    public async Task AnIdenticalRequestInsideTheWindow_GetsTheOriginalCorrelationId_AndIsNotQueuedAgain()
    {
        var (client, token) = await ClientWithCallbacksAsync(Single);

        var first = await GetStatsAsync(token, "jane@arbisoft.com", "24h");
        var repeat = await GetStatsAsync(token, "JANE@arbisoft.com ", "24h");

        Assert.Equal(HttpStatusCode.OK, repeat.Status);
        Assert.Equal(first.Correlation, repeat.Correlation);
        Assert.Equal("200", repeat.Json.GetProperty("status").GetString());
        Assert.Single(await Seed.LogsForAsync(client));
        Assert.Single(await RabbitMqHarness.DrainAsync(Factory.QueueName));
    }

    [BrokersFact]
    public async Task ADifferentTimeFrameEmailOrClient_IsNotADuplicate()
    {
        var (_, token) = await ClientWithCallbacksAsync(Single);
        var (_, otherToken) = await ClientWithCallbacksAsync(Single);

        var original = await GetStatsAsync(token, "jane@arbisoft.com", "24h");

        Assert.NotEqual(original.Correlation, (await GetStatsAsync(token, "jane@arbisoft.com", "1w")).Correlation);
        Assert.NotEqual(original.Correlation, (await GetStatsAsync(token, "john@arbisoft.com", "24h")).Correlation);
        Assert.NotEqual(original.Correlation, (await GetStatsAsync(otherToken, "jane@arbisoft.com", "24h")).Correlation);
        Assert.Equal(4, (await RabbitMqHarness.DrainAsync(Factory.QueueName)).Count);
    }

    [BrokersFact]
    public async Task OnceTheWindowHasPassed_TheSameRequestIsQueuedAgain_AndTheOldKeyIsReleased()
    {
        var (client, token) = await ClientWithCallbacksAsync(Single);
        var first = await GetStatsAsync(token, "jane@arbisoft.com");
        await Seed.ExecuteAsync("UPDATE RequestLogs SET createdOn = @old WHERE correlationId = @id", new { old = DateTime.UtcNow.AddMinutes(-16), id = first.Correlation });

        var again = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.NotEqual(first.Correlation, again.Correlation);
        Assert.Null((await Seed.LogAsync(first.Correlation))!.IdempotencyKey);
        Assert.NotNull((await Seed.LogAsync(again.Correlation))!.IdempotencyKey);
        Assert.Equal(2, (await Seed.LogsForAsync(client)).Count);
        Assert.Equal(2, (await RabbitMqHarness.DrainAsync(Factory.QueueName)).Count);
    }

    [BrokersFact]
    public async Task AFailedRequest_CanBeAskedForAgainStraightAway()
    {
        var (_, token) = await ClientWithCallbacksAsync(Single);
        var first = await GetStatsAsync(token, "jane@arbisoft.com");
        await Seed.ExecuteAsync("UPDATE RequestLogs SET status = @status WHERE correlationId = @id", new { status = RequestStatus.Failed, id = first.Correlation });

        var again = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.NotEqual(first.Correlation, again.Correlation);
    }

    [BrokersFact]
    public async Task TenIdenticalRequestsAtOnce_ProduceOneRequestAndOneMessage()
    {
        var (client, token) = await ClientWithCallbacksAsync(Single);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => GetStatsAsync(token, "race@arbisoft.com", "24h")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        Assert.Single(responses.Select(response => response.Correlation).Distinct());
        Assert.Single(await Seed.LogsForAsync(client));
        Assert.Single(await RabbitMqHarness.DrainAsync(Factory.QueueName));
    }

    [BrokersFact]
    public async Task WithNoCallbackUrl_TheRequestIsRevoked_With422AndTheContractBody_AndNothingIsQueued()
    {
        var (_, token) = await ClientWithCallbacksAsync();

        var response = await GetStatsAsync(token, "Jane@arbisoft.com", "24h");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
        Assert.Equal("422", response.Json.GetProperty("status").GetString());
        Assert.Equal("jane@arbisoft.com", response.Json.GetProperty("email").GetString());
        Assert.Equal("Revoked - no callback URL", response.Json.GetProperty("error").GetString());
        Assert.True(Guid.TryParse(response.Correlation, out _));

        var log = (await Seed.LogAsync(response.Correlation))!;
        Assert.Equal(RequestStatus.RevokedNoCallbackUrl, log.Status);
        Assert.Null(log.IdempotencyKey);
        Assert.NotNull(await Seed.LogJsonAsync(log.Id));

        // A revoked request never reaches the broker, so the queue was not even created.
        Assert.False(await RabbitMqHarness.QueueExistsAsync(Factory.QueueName));
    }

    [MySqlFact]
    public async Task ARevokedRequest_IsNotADuplicate_SoItCanBeSentAgainOnceACallbackExists()
    {
        var (client, token) = await ClientWithCallbacksAsync();

        var first = await GetStatsAsync(token, "jane@arbisoft.com");
        var second = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.Status);
        Assert.NotEqual(first.Correlation, second.Correlation);
        Assert.Equal(2, (await Seed.LogsForAsync(client)).Count);
    }

    [BrokersFact]
    public async Task ARevokedRequest_ThatIsLaterSentWithACallbackInPlace_IsQueued()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        var revoked = await GetStatsAsync(token, "jane@arbisoft.com");
        await Seed.SeedCallbackAsync(client, Single);

        var queued = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.Equal(HttpStatusCode.OK, queued.Status);
        Assert.NotEqual(revoked.Correlation, queued.Correlation);
        Assert.Single(await RabbitMqHarness.DrainAsync(Factory.QueueName));
    }

    [BrokersFact]
    public async Task ABatch_IsAcknowledged_WithNormalizedDistinctEmails_AndQueuedUnderOneCorrelationId()
    {
        var (client, token) = await ClientWithCallbacksAsync(Batch);

        var response = await PostBatchAsync(token, new { emails = new[] { "B@arbisoft.com", "a@arbisoft.com", " b@Arbisoft.com" }, timeFrame = "1w" });

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("200", response.Json.GetProperty("status").GetString());
        Assert.Equal(new[] { "b@arbisoft.com", "a@arbisoft.com" }, response.Json.GetProperty("emails").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(string.Empty, response.Json.GetProperty("error").GetString());
        Assert.False(response.Json.TryGetProperty("email", out _));

        var log = (await Seed.LogAsync(response.Correlation))!;
        Assert.Equal("engineer-stats-batch", log.EventType);
        Assert.Equal(RequestStatus.Queued, log.Status);
        var message = Assert.Single(await RabbitMqHarness.DrainAsync(Factory.QueueName));
        Assert.Equal("engineer-stats-batch", message.EventType);
        Assert.Equal(2, JsonDocument.Parse(message.Body).RootElement.GetProperty("emails").GetArrayLength());
        Assert.Single(await Seed.LogsForAsync(client));
    }

    [BrokersFact]
    public async Task ABatchOfExactlyTwentyEmails_IsAccepted()
    {
        var (_, token) = await ClientWithCallbacksAsync(Batch);

        Assert.Equal(HttpStatusCode.OK, (await PostBatchAsync(token, new { emails = Emails(20), timeFrame = "24h" })).Status);
    }

    [BrokersFact]
    public async Task ABatchWithTheSameEmailsInAnotherOrder_IsADuplicate()
    {
        var (_, token) = await ClientWithCallbacksAsync(Batch);

        var first = await PostBatchAsync(token, new { emails = new[] { "a@arbisoft.com", "b@arbisoft.com" }, timeFrame = "24h" });
        var second = await PostBatchAsync(token, new { emails = new[] { "b@arbisoft.com", "a@arbisoft.com" }, timeFrame = "24h" });

        Assert.Equal(first.Correlation, second.Correlation);
        Assert.Single(await RabbitMqHarness.DrainAsync(Factory.QueueName));
    }

    [MySqlFact]
    public async Task ABatchWithoutABatchCallbackUrl_Is422_EvenWhenTheSingleCallbackExists()
    {
        var (_, token) = await ClientWithCallbacksAsync(Single);

        var response = await PostBatchAsync(token, new { emails = new[] { "a@arbisoft.com", "b@arbisoft.com" }, timeFrame = "24h" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
        Assert.Equal("422", response.Json.GetProperty("status").GetString());
        Assert.Equal(2, response.Json.GetProperty("emails").GetArrayLength());
        Assert.Equal("Revoked - no callback URL", response.Json.GetProperty("error").GetString());
    }

    [MySqlFact]
    public async Task AnInvalidBatch_IsRejectedWith400_AndNothingIsRecorded()
    {
        var (client, token) = await ClientWithCallbacksAsync(Batch);
        var cases = new object?[]
        {
            new { emails = Emails(21), timeFrame = "24h" },
            new { emails = Array.Empty<string>(), timeFrame = "24h" },
            new { timeFrame = "24h" },
            new { emails = new[] { "a@arbisoft.com" } },
            new { emails = new[] { "a@arbisoft.com" }, timeFrame = "24H" },
            new { emails = new[] { "a@arbisoft.com", "b@gmail.com" }, timeFrame = "24h" },
            new { emails = new[] { "a@arbisoft.com" }, timeFrame = "2y" }
        };

        foreach (var body in cases)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PostBatchAsync(token, body)).Status);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token, "{ not json")).Status);
        Assert.Empty(await Seed.LogsForAsync(client));
    }

    [MySqlFact]
    public async Task ABatchError_NamesThePositionOfTheBadEmail_NotTheEmail()
    {
        var (_, token) = await ClientWithCallbacksAsync(Batch);

        var response = await PostBatchAsync(token, new { emails = new[] { "a@arbisoft.com", "private.person@gmail.com" }, timeFrame = "24h" });

        Assert.Contains("Email 2", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("private.person", response.Text, StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task ABatchTooLong_SaysSo()
    {
        var (_, token) = await ClientWithCallbacksAsync(Batch);

        var response = await PostBatchAsync(token, new { emails = Emails(21), timeFrame = "24h" });

        Assert.Contains("at most 20", response.Text, StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task ABatchSentAsTheWrongContentType_IsRejected()
    {
        var (_, token) = await ClientWithCallbacksAsync(Batch);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token, "emails=a@arbisoft.com&timeFrame=24h", "application/x-www-form-urlencoded");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.Status);
    }

    [MySqlFact]
    public async Task ATokenForAClientThatWasDeactivatedAfterwards_IsRejected()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetStatsAsync(token, "jane@arbisoft.com")).Status);

        await Seed.DeactivateClientAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetStatsAsync(token, "other@arbisoft.com")).Status);
    }
}
