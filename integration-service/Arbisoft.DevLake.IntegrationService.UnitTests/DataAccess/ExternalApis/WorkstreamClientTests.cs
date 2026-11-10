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
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public class WorkstreamClientTests
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly WorkstreamOptions _options = new() { BaseUrl = "https://ws.test/", ApiToken = "ws-token", PageSize = 2, MaxPages = 5 };

    private WorkstreamClient Create() => new(
        new HttpClient(_handler) { BaseAddress = new Uri(_options.BaseUrl) }, Options.Create(_options), NullLogger<WorkstreamClient>.Instance);

    [Fact]
    public async Task GetAllProjects_PagesArraysUntilAShortPage()
    {
        _handler.Respond(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]")
            .Respond(HttpStatusCode.OK, "[{\"id\":3}]");

        var projects = await Create().GetAllProjectsAsync();

        Assert.Equal([1, 2, 3], projects.Select(p => p.GetProperty("id").GetInt32()));
        Assert.Equal(["?page=1&pageSize=2", "?page=2&pageSize=2"], _handler.Requests.Select(r => r.Uri.Query));
        Assert.All(_handler.Requests, r => Assert.Equal("Bearer ws-token", r.Headers["Authorization"]));
    }

    [Fact]
    public async Task GetAllProjects_StopsOnAnEmptyArrayPage()
    {
        _handler.Respond(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]").Respond(HttpStatusCode.OK, "[]");

        Assert.Equal(2, (await Create().GetAllProjectsAsync()).Count);
    }

    [Fact]
    public async Task GetAllProjects_FollowsResultsPagesUntilNextIsNull()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"results\":[{\"id\":1},{\"id\":2}],\"next\":\"page2\"}")
            .Respond(HttpStatusCode.OK, "{\"results\":[{\"id\":3},{\"id\":4}],\"next\":null}");

        var projects = await Create().GetAllProjectsAsync();

        Assert.Equal(4, projects.Count);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task GetAllProjects_FailsRatherThanReturningAPartialList()
    {
        _handler.Respond(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]").Respond(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<ExternalApiException>(() => Create().GetAllProjectsAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task GetAllProjects_StopsAtMaxPages()
    {
        for (var i = 0; i < 5; i++)
        {
            _handler.Respond(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]");
        }

        var exception = await Assert.ThrowsAsync<ExternalApiException>(() => Create().GetAllProjectsAsync());
        Assert.Contains("more than 5 pages", exception.Message);
    }

    [Fact]
    public async Task GetAllProjects_RejectsAnUnknownPageShape()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"items\":[]}");

        await Assert.ThrowsAsync<ExternalApiException>(() => Create().GetAllProjectsAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "true", true)]
    [InlineData(HttpStatusCode.OK, "false", false)]
    [InlineData(HttpStatusCode.Unauthorized, "", false)]
    public async Task IsTokenValid_ReportsTheTokenState(HttpStatusCode status, string body, bool expected)
    {
        _handler.Respond(status, body);

        Assert.Equal(expected, await Create().IsTokenValidAsync());
        Assert.Equal("https://ws.test/is_valid", Assert.Single(_handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Calls_ExplainAMissingToken()
    {
        _options.ApiToken = " ";

        var exception = await Assert.ThrowsAsync<ExternalApiAuthenticationException>(() => Create().GetAllProjectsAsync());
        Assert.Contains("Workstream__ApiToken", exception.Message);
    }
}
