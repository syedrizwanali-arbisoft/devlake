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

using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Services.Projects;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Projects;

public class ProjectMapperTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void AProjectWithANestedTeamLead_IsMapped()
    {
        Assert.True(ProjectMapper.TryMap(Json("{\"id\":\"p-1\",\"name\":\"Apollo\",\"team_lead\":{\"name\":\"Jane Doe\",\"email\":\"Jane.Doe@Arbisoft.com\"}}"), out var project, out var problem));

        Assert.Null(problem);
        Assert.Equal("p-1", project!.Id);
        Assert.Equal("Apollo", project.Name);
        Assert.Equal("Jane Doe", project.TeamLead!.Name);
        Assert.Equal("jane.doe@arbisoft.com", project.TeamLead.Email);
    }

    [Theory]
    [InlineData("{\"project_id\":7,\"title\":\"Gemini\"}", "7", "Gemini")]
    [InlineData("{\"projectId\":\"x\",\"projectName\":\"Mercury\"}", "x", "Mercury")]
    [InlineData("{\"uuid\":\"u-1\",\"name\":\"Vostok\"}", "u-1", "Vostok")]
    [InlineData("{\"id\":\"  p-2  \",\"name\":\"  Soyuz  \"}", "p-2", "Soyuz")]
    [InlineData("{\"id\":42,\"name\":\"Skylab\"}", "42", "Skylab")]
    public void TheCommonNamesForIdAndName_AreAccepted_AndTrimmed(string json, string id, string name)
    {
        Assert.True(ProjectMapper.TryMap(Json(json), out var project, out _));

        Assert.Equal(id, project!.Id);
        Assert.Equal(name, project.Name);
    }

    [Fact]
    public void WhenSeveralNamesArePresent_TheFirstInTheFixedOrderWins()
    {
        Assert.True(ProjectMapper.TryMap(Json("{\"title\":\"Second\",\"name\":\"First\",\"uuid\":\"u\",\"id\":\"i\"}"), out var project, out _));

        Assert.Equal("i", project!.Id);
        Assert.Equal("First", project.Name);
    }

    [Theory]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"teamLead\":{\"full_name\":\"Ann\",\"email_address\":\"ANN@x.example\"}}", "Ann", "ann@x.example")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"lead\":{\"displayName\":\"Bob\"}}", "Bob", null)]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"manager\":{\"email\":\"cy@x.example\"}}", null, "cy@x.example")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":\"Dee Dee\"}", "Dee Dee", null)]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":\"Eve@X.example\"}", null, "eve@x.example")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead_name\":\"Fay\",\"team_lead_email\":\"FAY@x.example\"}", "Fay", "fay@x.example")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"teamLeadName\":\"Gus\"}", "Gus", null)]
    public void TheTeamLead_IsReadFromWhicheverShapeWorkstreamUses(string json, string? name, string? email)
    {
        Assert.True(ProjectMapper.TryMap(Json(json), out var project, out _));

        Assert.Equal(name, project!.TeamLead!.Name);
        Assert.Equal(email, project.TeamLead.Email);
    }

    [Theory]
    [InlineData("{\"id\":\"1\",\"name\":\"A\"}")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":null}")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":{}}")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":\"   \"}")]
    [InlineData("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":{\"email\":\"not-an-email\"}}")]
    public void AProjectWithoutAUsableTeamLead_StillMaps_WithNoTeamLead(string json)
    {
        Assert.True(ProjectMapper.TryMap(Json(json), out var project, out _));

        Assert.Null(project!.TeamLead);
    }

    [Theory]
    [InlineData("{\"name\":\"A\"}", "no id")]
    [InlineData("{\"id\":\"\",\"name\":\"A\"}", "no id")]
    [InlineData("{\"id\":\"   \",\"name\":\"A\"}", "no id")]
    [InlineData("{\"id\":null,\"name\":\"A\"}", "no id")]
    [InlineData("{\"id\":{\"x\":1},\"name\":\"A\"}", "no id")]
    [InlineData("{\"id\":\"1\"}", "no name")]
    [InlineData("{\"id\":\"1\",\"name\":\"\"}", "no name")]
    [InlineData("{\"id\":\"1\",\"name\":[\"A\"]}", "no name")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("\"text\"", "not a JSON object")]
    [InlineData("42", "not a JSON object")]
    [InlineData("null", "not a JSON object")]
    public void AProjectThatCannotBeUsed_IsReported_NeverGuessedAt(string json, string expectedProblem)
    {
        Assert.False(ProjectMapper.TryMap(Json(json), out var project, out var problem));

        Assert.Null(project);
        Assert.Contains(expectedProblem, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheAllowListedFieldsLeave_NotWhateverElseWorkstreamSends()
    {
        Assert.True(ProjectMapper.TryMap(Json("{\"id\":\"1\",\"name\":\"A\",\"budget\":1000000,\"internal_notes\":\"secret\",\"team_lead\":{\"name\":\"L\",\"phone\":\"123\",\"salary\":9}}"), out var project, out _));

        var serialized = JsonSerializer.Serialize(project);

        Assert.DoesNotContain("budget", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("phone", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("salary", serialized, StringComparison.Ordinal);
        Assert.Equal("{\"id\":\"1\",\"name\":\"A\",\"team_lead\":{\"name\":\"L\",\"email\":null}}", serialized);
    }
}
