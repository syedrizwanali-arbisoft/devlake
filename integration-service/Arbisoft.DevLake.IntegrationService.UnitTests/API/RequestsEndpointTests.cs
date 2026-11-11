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
using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

/// <summary>GET /api/v1/requests/poll/{uuid} end to end: real HTTP pipeline and MySQL.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed partial class RequestsEndpointTests : ApiTestBase
{
    public RequestsEndpointTests(MySqlDatabaseFixture database)
        : base(database)
    {
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")]
    private static partial Regex CheckTimeShape();

    private Task<ApiResponse> PollAsync(string? token, string uuid) => SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{uuid}", token);

    [MySqlFact]
    public async Task EveryStatus_IsReportedWithTheDocumentedName()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        var expected = new Dictionary<string, string>
        {
            [RequestStatus.Received] = "Queued",
            [RequestStatus.Queued] = "Queued",
            [RequestStatus.Processing] = "Processing",
            [RequestStatus.Processed] = "Processing",
            [RequestStatus.Success] = "Success",
            [RequestStatus.Failed] = "Failure",
            [RequestStatus.RevokedNoCallbackUrl] = "Revoked"
        };

        foreach (var (stored, shown) in expected)
        {
            var correlationId = await Seed.SeedRequestAsync(client, stored);

            var response = await PollAsync(token, correlationId);

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(shown, response.Json.GetProperty("status").GetString());
        }
    }

    [MySqlFact]
    public async Task TheResponse_HasExactlyTheThreeDocumentedFields()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        var correlationId = await Seed.SeedRequestAsync(client, RequestStatus.Processing);

        var response = await PollAsync(token, correlationId);

        Assert.Equal(["check_time", "correlation_id", "status"], response.Json.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(correlationId, response.Json.GetProperty("correlation_id").GetString());
        var checkTime = response.Json.GetProperty("check_time").GetString()!;
        Assert.Matches(CheckTimeShape(), checkTime);
        Assert.InRange(DateTimeOffset.Parse(checkTime, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [MySqlFact]
    public async Task TheIdMayBeWrittenInAnyCase()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        var correlationId = await Seed.SeedRequestAsync(client, RequestStatus.Success);

        var response = await PollAsync(token, correlationId.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(correlationId, response.Json.GetProperty("correlation_id").GetString());
    }

    [MySqlFact]
    public async Task AnUnknownId_AndAnotherClientsId_GetTheSame404_SoExistenceIsNotRevealed()
    {
        var (_, token) = await ClientWithCallbacksAsync();
        var (otherClient, _) = await ClientWithCallbacksAsync();
        var othersRequest = await Seed.SeedRequestAsync(otherClient, RequestStatus.Success);

        var unknown = await PollAsync(token, Guid.NewGuid().ToString());
        var notYours = await PollAsync(token, othersRequest);

        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal(HttpStatusCode.NotFound, notYours.Status);
        Assert.Equal(unknown.Json.GetProperty("title").GetString(), notYours.Json.GetProperty("title").GetString());
        Assert.Equal(404, notYours.Json.GetProperty("status").GetInt32());
        Assert.True(notYours.Json.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("Success", notYours.Text, StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task AnIdThatIsNotAUuid_Is400()
    {
        var (_, token) = await ClientWithCallbacksAsync();

        foreach (var bad in new[] { "not-a-uuid", "123", "0b8f3c52-1111-4222-8333-44445555666" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PollAsync(token, bad)).Status);
        }
    }

    [MySqlFact]
    public async Task WithoutAToken_Is401_AndWithoutTheStatsReadScope_Is403()
    {
        var client = await Seed.SeedClientAsync(scopes: ["projects:fetch"]);
        var token = await ApiSeed.RequestTokenAsync(Http, client);
        var correlationId = await Seed.SeedRequestAsync(client, RequestStatus.Success);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PollAsync(null, correlationId)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await PollAsync(token, correlationId)).Status);
    }

    [MySqlFact]
    public async Task ARevokedRequest_ReportsRevoked_AfterTheStatsCallThatCreatedIt()
    {
        var (_, token) = await ClientWithCallbacksAsync();
        var stats = await GetStatsAsync(token, "jane@arbisoft.com");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, stats.Status);

        var poll = await PollAsync(token, stats.Correlation);

        Assert.Equal(HttpStatusCode.OK, poll.Status);
        Assert.Equal("Revoked", poll.Json.GetProperty("status").GetString());
    }

    [BrokersFact]
    public async Task AQueuedRequest_ReportsQueued_ThenFollowsItsProgress()
    {
        var (_, token) = await ClientWithCallbacksAsync(RequestEventTypes.EngineerStats);
        var stats = await GetStatsAsync(token, "jane@arbisoft.com");

        Assert.Equal("Queued", (await PollAsync(token, stats.Correlation)).Json.GetProperty("status").GetString());

        await Seed.ExecuteAsync("UPDATE RequestLogs SET status = @status WHERE correlationId = @id", new { status = RequestStatus.Processing, id = stats.Correlation });
        Assert.Equal("Processing", (await PollAsync(token, stats.Correlation)).Json.GetProperty("status").GetString());

        await Seed.ExecuteAsync("UPDATE RequestLogs SET status = @status WHERE correlationId = @id", new { status = RequestStatus.Success, id = stats.Correlation });
        Assert.Equal("Success", (await PollAsync(token, stats.Correlation)).Json.GetProperty("status").GetString());
    }

    [MySqlFact]
    public async Task ATokenForAClientThatWasDeactivated_Is401()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        var correlationId = await Seed.SeedRequestAsync(client, RequestStatus.Success);
        Assert.Equal(HttpStatusCode.OK, (await PollAsync(token, correlationId)).Status);

        await Seed.DeactivateClientAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PollAsync(token, correlationId)).Status);
    }
}

/// <summary>The poll API has a rate limit of its own, separate from the stats APIs.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class PollRateLimitTests : ApiTestBase
{
    public PollRateLimitTests(MySqlDatabaseFixture database)
        : base(database, new Dictionary<string, string> { ["RateLimits:PollPerMinute"] = "4" })
    {
    }

    [MySqlFact]
    public async Task PastItsLimit_ThePollApiAnswers429WithRetryAfter_WhileTheStatsApiStillWorks()
    {
        var (client, token) = await ClientWithCallbacksAsync();
        var correlationId = await Seed.SeedRequestAsync(client, RequestStatus.Success);
        var poll = $"/api/v1/requests/poll/{correlationId}";

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, poll, token)).Status);
        }

        var limited = await SendAsync(HttpMethod.Get, poll, token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limited", limited.Json.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetStatsAsync(token, "jane@arbisoft.com")).Status);
    }
}
