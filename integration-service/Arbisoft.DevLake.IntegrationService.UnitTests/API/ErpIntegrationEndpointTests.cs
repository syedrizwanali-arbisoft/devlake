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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

/// <summary>GET /fetch/projects end to end: the real HTTP pipeline, authorization and MySQL, with Workstream stubbed.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class ErpIntegrationEndpointTests : ApiTestBase
{
    private const int PageSize = 10;

    private readonly UpstreamStub _workstream;

    public ErpIntegrationEndpointTests(MySqlDatabaseFixture database)
        : base(database)
    {
        _workstream = new UpstreamStub(Upstream);
    }

    private static string Items(int from, int count) => "[" + string.Join(",", Enumerable.Range(from, count).Select(i =>
        $"{{\"id\":\"p{i}\",\"name\":\"Project {i}\",\"budget\":999,\"team_lead\":{{\"name\":\"Lead {i}\",\"email\":\"Lead{i}@Arbisoft.com\",\"phone\":\"123\"}}}}")) + "]";

    private void GivenPages(int total)
    {
        _workstream.WorkstreamPages = page =>
        {
            var start = ((page - 1) * PageSize) + 1;
            var count = Math.Clamp(total - start + 1, 0, PageSize);
            return (HttpStatusCode.OK, Items(start, count));
        };
    }

    private Task<ApiResponse> FetchAsync(string? token) => SendAsync(HttpMethod.Get, "/fetch/projects", token);

    private async Task<string> ProjectsTokenAsync()
    {
        return await ApiSeed.RequestTokenAsync(Http, await Seed.SeedClientAsync());
    }

    [MySqlFact]
    public async Task EveryPage_IsReadAndMerged_IntoOneList_WithTheTeamLeads()
    {
        GivenPages(24);

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var projects = response.Json.EnumerateArray().ToList();
        Assert.Equal(24, projects.Count);
        Assert.Equal(Enumerable.Range(1, 24).Select(i => $"p{i}"), projects.Select(p => p.GetProperty("id").GetString()));
        Assert.Equal("Project 7", projects[6].GetProperty("name").GetString());
        Assert.Equal("Lead 7", projects[6].GetProperty("team_lead").GetProperty("name").GetString());
        Assert.Equal("lead7@arbisoft.com", projects[6].GetProperty("team_lead").GetProperty("email").GetString());
        Assert.Equal(3, _workstream.WorkstreamProjectRequests.Count());
    }

    [MySqlFact]
    public async Task OnlyTheDocumentedFieldsLeave_WhateverElseWorkstreamSends()
    {
        GivenPages(2);

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.DoesNotContain("budget", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("phone", response.Text, StringComparison.Ordinal);
        var project = response.Json[0];
        Assert.Equal(["id", "name", "team_lead"], project.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["email", "name"], project.GetProperty("team_lead").EnumerateObject().Select(p => p.Name).Order());
    }

    [MySqlFact]
    public async Task TheTokenIsCheckedFirst_ThenPagesAreAskedForWithTheConfiguredSize_AndTheCorrelationIdTravelsOn()
    {
        GivenPages(12);

        var response = await FetchAsync(await ProjectsTokenAsync());

        var check = Assert.Single(_workstream.WorkstreamTokenChecks);
        Assert.Equal("Bearer token", check.Authorization);
        var pages = _workstream.WorkstreamProjectRequests.ToList();
        Assert.Equal(["page=1&pageSize=10", "page=2&pageSize=10"], pages.Select(p => p.Uri.Query.TrimStart('?')));
        Assert.All(pages, page => Assert.Equal("Bearer token", page.Authorization));
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.Equal(correlationId, check.CorrelationId);
        Assert.All(pages, page => Assert.Equal(correlationId, page.CorrelationId));
    }

    [MySqlFact]
    public async Task AnEmptyWorkstream_IsAnEmptyList_NotAnError()
    {
        GivenPages(0);

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(0, response.Json.GetArrayLength());
    }

    [MySqlFact]
    public async Task WithoutTheProjectsFetchScope_TheCallIsForbidden_AndWorkstreamIsNeverCalled()
    {
        var statsOnly = await ApiSeed.RequestTokenAsync(Http, await Seed.SeedClientAsync(scopes: ["stats:read"]));

        Assert.Equal(HttpStatusCode.Forbidden, (await FetchAsync(statsOnly)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await FetchAsync(null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await FetchAsync("not.a.token")).Status);
        Assert.Empty(_workstream.WorkstreamTokenChecks);
        Assert.Empty(_workstream.WorkstreamProjectRequests);
    }

    [MySqlFact]
    public async Task WhenWorkstreamRejectsOurToken_TheAnswerIsAClearError_AndNoPageIsRead()
    {
        _workstream.WorkstreamTokenValid = false;

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.Status);
        Assert.Equal("workstream_token_invalid", response.Json.GetProperty("code").GetString());
        Assert.Contains("token", response.Json.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.True(response.Json.TryGetProperty("correlationId", out _));
        Assert.Empty(_workstream.WorkstreamProjectRequests);
    }

    [MySqlFact]
    public async Task AFailurePartWay_ReturnsAnError_NotAPartialList()
    {
        _workstream.WorkstreamPages = page => page == 1 ? (HttpStatusCode.OK, Items(1, PageSize)) : (HttpStatusCode.InternalServerError, "{}");

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal("upstream_unavailable", response.Json.GetProperty("code").GetString());
        Assert.DoesNotContain("Project 1", response.Text, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, response.Json.ValueKind);
    }

    [MySqlFact]
    public async Task APageThatKeepsFailing_IsRetried_BeforeTheCallGivesUp()
    {
        _workstream.WorkstreamPages = page => page == 1 ? (HttpStatusCode.OK, Items(1, PageSize)) : (HttpStatusCode.BadGateway, "{}");

        await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(1 + 4, _workstream.WorkstreamProjectRequests.Count());
    }

    [MySqlFact]
    public async Task APageWorkstreamRefusesOutright_IsNotRetried_AndIsAnUpstreamRejection()
    {
        _workstream.WorkstreamPages = page => page == 1 ? (HttpStatusCode.OK, Items(1, PageSize)) : (HttpStatusCode.BadRequest, "{\"error\":\"bad page\"}");

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.Status);
        Assert.Equal("upstream_rejected", response.Json.GetProperty("code").GetString());
        Assert.Equal(2, _workstream.WorkstreamProjectRequests.Count());
    }

    [MySqlFact]
    public async Task WhenWorkstreamRejectsTheTokenPartWay_ItIsACredentialsError()
    {
        _workstream.WorkstreamPages = page => page == 1 ? (HttpStatusCode.OK, Items(1, PageSize)) : (HttpStatusCode.Unauthorized, "{}");

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.Status);
        Assert.Equal("upstream_credentials", response.Json.GetProperty("code").GetString());
    }

    [MySqlFact]
    public async Task AnAnswerThatIsNotAListOfProjects_IsABadResponse()
    {
        _workstream.WorkstreamPages = _ => (HttpStatusCode.OK, "{\"message\":\"maintenance\"}");

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.Status);
        Assert.Equal("upstream_bad_response", response.Json.GetProperty("code").GetString());
    }

    [MySqlFact]
    public async Task AProjectThatCannotBeRead_FailsTheCall_WithoutRevealingItsContent()
    {
        _workstream.WorkstreamPages = _ => (HttpStatusCode.OK, "[{\"id\":\"p1\",\"name\":\"Fine\"},{\"name\":\"Confidential Codename With No Id\"}]");

        var response = await FetchAsync(await ProjectsTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.Status);
        Assert.Equal("upstream_bad_response", response.Json.GetProperty("code").GetString());
        Assert.DoesNotContain("Confidential", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fine", response.Text, StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task ManyCallsAtOnce_EachGetTheWholeList()
    {
        GivenPages(15);
        var token = await ProjectsTokenAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => FetchAsync(token)));

        Assert.All(responses, response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(15, response.Json.GetArrayLength());
        });
    }

    [MySqlFact]
    public async Task TheRouteIsNotVersioned_SoOnlyFetchProjectsExists()
    {
        GivenPages(1);
        var token = await ProjectsTokenAsync();

        Assert.Equal(HttpStatusCode.OK, (await FetchAsync(token)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, "/api/v1/fetch/projects", token)).Status);
    }

    [MySqlFact]
    public async Task ATokenForAClientThatWasDeactivated_IsRefused()
    {
        GivenPages(1);
        var client = await Seed.SeedClientAsync();
        var token = await ApiSeed.RequestTokenAsync(Http, client);
        Assert.Equal(HttpStatusCode.OK, (await FetchAsync(token)).Status);

        await Seed.DeactivateClientAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await FetchAsync(token)).Status);
    }
}
