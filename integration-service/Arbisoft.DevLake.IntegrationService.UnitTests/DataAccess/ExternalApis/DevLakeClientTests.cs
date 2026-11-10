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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public class DevLakeClientTests
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly DevLakeOptions _options = new() { BaseUrl = "http://devlake.test/api/", ApiToken = "dl-token" };

    private DevLakeClient Create() => new(
        new HttpClient(_handler) { BaseAddress = new Uri(_options.BaseUrl) }, Options.Create(_options), NullLogger<DevLakeClient>.Instance);

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task IsTokenValid_ReadsTheBody(string body, bool expected)
    {
        _handler.Respond(HttpStatusCode.OK, body);

        Assert.Equal(expected, await Create().IsTokenValidAsync("corr"));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("http://devlake.test/api/is_valid", request.Uri.ToString());
        Assert.Equal("Bearer dl-token", request.Headers["Authorization"]);
        Assert.Equal("corr", request.Headers["X-Correlation-ID"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task IsTokenValid_IsFalseWhenDevLakeRejectsTheToken(HttpStatusCode status)
    {
        _handler.Respond(status);

        Assert.False(await Create().IsTokenValidAsync());
    }

    [Fact]
    public async Task IsTokenValid_ThrowsForAServerError()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, "boom");

        var exception = await Assert.ThrowsAsync<ExternalApiException>(() => Create().IsTokenValidAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.StartsWith("DevLake:", exception.Message);
    }

    [Fact]
    public async Task IsTokenValid_RejectsAnUnexpectedBody()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"valid\":true}");

        await Assert.ThrowsAsync<ExternalApiException>(() => Create().IsTokenValidAsync());
    }

    [Theory]
    [InlineData(DevLakeStatsCategory.Jira, "jira")]
    [InlineData(DevLakeStatsCategory.Github, "github")]
    [InlineData(DevLakeStatsCategory.AiUsage, "ai_usage")]
    [InlineData(DevLakeStatsCategory.AiCost, "ai_cost")]
    public async Task GetStats_CallsTheCategoryPathWithTheTimeFrame(DevLakeStatsCategory category, string segment)
    {
        _handler.Respond(HttpStatusCode.OK, "{\"count\":3}");

        var stats = await Create().GetStatsAsync("a.b+c@arbisoft.com", category, "11M", "corr");

        Assert.Equal(3, stats.GetProperty("count").GetInt32());
        var request = Assert.Single(_handler.Requests);
        Assert.Equal($"/api/stats/a.b%2Bc%40arbisoft.com/{segment}", request.Uri.AbsolutePath);
        Assert.Equal("?tf=11M", request.Uri.Query);
        Assert.Equal("Bearer dl-token", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task GetStats_ReportsARejectedTokenAsAnAuthenticationFailure()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<ExternalApiAuthenticationException>(
            () => Create().GetStatsAsync("a@arbisoft.com", DevLakeStatsCategory.Jira, "1d", "corr"));
        Assert.Contains("token was rejected", exception.Message);
    }

    [Fact]
    public async Task GetStats_ThrowsForANonJsonBody()
    {
        _handler.Respond(HttpStatusCode.OK, "<html>", "text/html");

        await Assert.ThrowsAsync<ExternalApiException>(
            () => Create().GetStatsAsync("a@arbisoft.com", DevLakeStatsCategory.Jira, "1d", "corr"));
    }

    [Fact]
    public async Task GetStats_WrapsTransportFailures()
    {
        _handler.Throw(new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<ExternalApiException>(
            () => Create().GetStatsAsync("a@arbisoft.com", DevLakeStatsCategory.Jira, "1d", "corr"));
        Assert.Contains("connection refused", exception.Message);
    }

    [Fact]
    public async Task Calls_ExplainAMissingToken()
    {
        _options.ApiToken = "";

        var exception = await Assert.ThrowsAsync<ExternalApiAuthenticationException>(() => Create().IsTokenValidAsync());
        Assert.Contains("DevLake__ApiToken", exception.Message);
        Assert.Empty(_handler.Requests);
    }
}
