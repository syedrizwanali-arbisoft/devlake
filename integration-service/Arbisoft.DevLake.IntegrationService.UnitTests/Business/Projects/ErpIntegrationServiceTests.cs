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
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;
using Arbisoft.DevLake.IntegrationService.Business.Services.Projects;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Projects;

public class ErpIntegrationServiceTests
{
    private readonly IWorkstreamClient _workstream = Substitute.For<IWorkstreamClient>();
    private readonly ErpIntegrationService _service;

    public ErpIntegrationServiceTests()
    {
        _workstream.IsTokenValidAsync(Arg.Any<CancellationToken>()).Returns(true);
        _service = new ErpIntegrationService(_workstream, NullLogger<ErpIntegrationService>.Instance);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task GetProjects_ChecksTheTokenThenReturnsEveryProject()
    {
        _workstream.GetAllProjectsAsync(Arg.Any<CancellationToken>()).Returns([
            Json("""{"id": 7, "name": "DevLake", "team_lead": {"name": "Ubaid", "email": "Ubaid@Arbisoft.com"}}"""),
            Json("""{"id": "p-2", "title": "Sophia"}""")]);

        var result = await _service.GetProjectsAsync();

        Assert.Equal(ProjectsOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Response!.Count);
        var first = result.Response.Projects[0];
        Assert.Equal(("7", "DevLake"), (first.Id, first.Name));
        Assert.Equal(new TeamLeadDto("Ubaid", "ubaid@arbisoft.com"), first.TeamLead);
        Assert.Equal(("p-2", "Sophia", (TeamLeadDto?)null), (result.Response.Projects[1].Id, result.Response.Projects[1].Name, result.Response.Projects[1].TeamLead));
        Received.InOrder(() =>
        {
            _workstream.IsTokenValidAsync(Arg.Any<CancellationToken>());
            _workstream.GetAllProjectsAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task GetProjects_StopsWithAClearErrorWhenTheTokenIsRejected()
    {
        _workstream.IsTokenValidAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.GetProjectsAsync();

        Assert.Equal(ProjectsOutcome.TokenRejected, result.Outcome);
        Assert.Contains("rejected", result.Error);
        await _workstream.DidNotReceiveWithAnyArgs().GetAllProjectsAsync(default);
    }

    [Fact]
    public async Task GetProjects_ReportsAMissingTokenSetting()
    {
        _workstream.IsTokenValidAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalApiAuthenticationException("Workstream", "Workstream:ApiToken is not set."));

        var result = await _service.GetProjectsAsync();

        Assert.Equal(ProjectsOutcome.TokenRejected, result.Outcome);
        Assert.Contains("Workstream:ApiToken", result.Error);
    }

    [Fact]
    public async Task GetProjects_ReturnsAnErrorInsteadOfAPartialList()
    {
        _workstream.GetAllProjectsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalApiException("Workstream", "/projects returned 503.", HttpStatusCode.ServiceUnavailable));

        var result = await _service.GetProjectsAsync();

        Assert.Equal(new ProjectsResult(ProjectsOutcome.Unavailable, null, "Workstream: /projects returned 503."), result);
    }

    [Theory]
    [InlineData("""{"project_id": 3, "project_name": "A", "teamLead": {"full_name": "Lead", "email": "l@arbisoft.com"}}""", "3", "A", "Lead", "l@arbisoft.com")]
    [InlineData("""{"projectId": "x", "name": " B ", "team_lead_name": "Flat", "team_lead_email": "F@arbisoft.com"}""", "x", "B", "Flat", "f@arbisoft.com")]
    [InlineData("""{"id": 1, "name": "C", "lead": {"name": "", "email": "e@arbisoft.com"}}""", "1", "C", null, "e@arbisoft.com")]
    public void ToDto_MapsTheCommonFieldNames(string json, string id, string name, string? leadName, string leadEmail)
    {
        var dto = ErpIntegrationService.ToDto(Json(json));

        Assert.Equal((id, name), (dto.Id, dto.Name));
        Assert.Equal(new TeamLeadDto(leadName, leadEmail), dto.TeamLead);
        Assert.Equal(JsonValueKind.Object, dto.Source.ValueKind);
    }

    [Fact]
    public void ToDto_KeepsAnItemItCannotMap()
    {
        var dto = ErpIntegrationService.ToDto(Json("\"just a string\""));

        Assert.Equal((null, null, (TeamLeadDto?)null), (dto.Id, dto.Name, dto.TeamLead));
        Assert.Equal("just a string", dto.Source.GetString());
    }
}
