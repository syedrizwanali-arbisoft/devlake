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
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Controllers;

/// <summary>The stats intake APIs on the real pipeline: scope, rate limits, status codes and the contract's body.</summary>
public sealed class StatsApiPipelineTests : IDisposable
{
    private readonly ApiTestHost _host = new(new Dictionary<string, string?>
    {
        ["RateLimits:StatsPerMinute"] = "3",
        ["RateLimits:BatchStatsPerMinute"] = "2",
    });

    public StatsApiPipelineTests()
    {
        _host.ClientConfigs.GetActiveOutboundUrlAsync(1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClientConfigOutboundUrl { Id = 1, IsActive = true });
        _host.RequestLogs.AddAsync(Arg.Any<RequestLog>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(42L);
    }

    public void Dispose() => _host.Dispose();

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task SingleStats_ReturnsTheAcknowledgementAndQueuesTheRequest()
    {
        var token = await _host.GetTokenAsync();

        using var response = await _host.SendAsync(HttpMethod.Get, "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("200", body.GetProperty("status").GetString());
        Assert.Equal("abc@arbisoft.com", body.GetProperty("email").GetString());
        Assert.Equal("", body.GetProperty("error").GetString());
        var correlationId = body.GetProperty("correlation_id").GetString()!;
        Assert.True(Guid.TryParse(correlationId, out _));
        await _host.Publisher.Received(1).PublishAsync(correlationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _host.RequestLogs.Received(1).AddAsync(
            Arg.Is<RequestLog>(l => l.Url == "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h" && l.EventType == "engineer-stats"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BatchStats_ReturnsTheAcknowledgement()
    {
        var token = await _host.GetTokenAsync();

        using var response = await _host.SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token,
            Json("""{ "emails": ["abc@arbisoft.com", "def@arbisoft.com"], "timeFrame": "1w" }"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("200", body.GetProperty("status").GetString());
        Assert.Equal(["abc@arbisoft.com", "def@arbisoft.com"], body.GetProperty("emails").EnumerateArray().Select(e => e.GetString()));
        Assert.True(Guid.TryParse(body.GetProperty("correlation_id").GetString(), out _));
    }

    [Fact]
    public async Task Stats_Returns422WithTheContractBodyWhenThereIsNoCallback()
    {
        _host.ClientConfigs.GetActiveOutboundUrlAsync(1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ClientConfigOutboundUrl?)null);
        var token = await _host.GetTokenAsync();

        using var response = await _host.SendAsync(HttpMethod.Get, "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("422", body.GetProperty("status").GetString());
        Assert.Equal("Revoked - no callback URL", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("correlation_id").GetString()));
        await _host.Publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("/api/v1/engineers/abc@arbisoft.com/stats?tf=24H")]
    [InlineData("/api/v1/engineers/abc@arbisoft.com/stats")]
    [InlineData("/api/v1/engineers/abc@gmail.com/stats?tf=24h")]
    public async Task SingleStats_Returns400ForInvalidInput(string path)
    {
        var token = await _host.GetTokenAsync();

        using var response = await _host.SendAsync(HttpMethod.Get, path, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("400", body.GetProperty("status").GetString());
        Assert.NotEqual("", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "emails": "abc@arbisoft.com", "timeFrame": "1w" }""")]
    [InlineData("""{ "emails": [], "timeFrame": "1w" }""")]
    public async Task BatchStats_Returns400ForABadBody(string body)
    {
        var token = await _host.GetTokenAsync();

        using var response = await _host.SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("400", (await BodyAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task BatchStats_Returns400ForMoreThan20Emails()
    {
        var token = await _host.GetTokenAsync();
        var emails = string.Join(",", Enumerable.Range(1, 21).Select(i => $"\"e{i}@arbisoft.com\""));

        using var response = await _host.SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token,
            Json($$"""{ "emails": [{{emails}}], "timeFrame": "1w" }"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stats_RequireTheStatsReadScope()
    {
        _host.GrantScopes("projects:fetch");
        var token = await _host.GetTokenAsync();

        using var single = await _host.SendAsync(HttpMethod.Get, "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h", token);
        using var batch = await _host.SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token,
            Json("""{ "emails": ["abc@arbisoft.com"], "timeFrame": "1w" }"""));
        using var anonymous = await _host.SendAsync(HttpMethod.Get, "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h", null);

        Assert.Equal(HttpStatusCode.Forbidden, single.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, batch.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Stats_ReturnsA429OverThePerClientLimit()
    {
        var token = await _host.GetTokenAsync();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await _host.SendAsync(HttpMethod.Get, $"/api/v1/engineers/e{i}@arbisoft.com/stats?tf=24h", token);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);

        // The batch API keeps its own, separate limit.
        using var batch = await _host.SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token,
            Json("""{ "emails": ["abc@arbisoft.com"], "timeFrame": "1w" }"""));
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
    }

    [Fact]
    public async Task BatchStats_ReturnsA429OverItsLowerLimit()
    {
        var token = await _host.GetTokenAsync();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await _host.SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token,
                Json($$"""{ "emails": ["e{{i}}@arbisoft.com"], "timeFrame": "1w" }"""));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
