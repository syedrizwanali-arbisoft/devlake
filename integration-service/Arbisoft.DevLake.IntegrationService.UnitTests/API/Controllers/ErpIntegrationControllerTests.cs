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
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Controllers;

/// <summary>GET /fetch/projects on the real pipeline, with Workstream replaced.</summary>
public sealed class ErpIntegrationControllerTests : IDisposable
{
    private readonly ApiTestHost _host = new();

    public ErpIntegrationControllerTests()
    {
        _host.GrantScopes("projects:fetch");
        _host.Workstream.IsTokenValidAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _host.Dispose();

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string? token)
    {
        using var response = await _host.SendAsync(HttpMethod.Get, "/fetch/projects", token);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task ReturnsEveryProjectWithItsTeamLead()
    {
        _host.Workstream.GetAllProjectsAsync(Arg.Any<CancellationToken>()).Returns([
            JsonDocument.Parse("""{"id": 7, "name": "DevLake", "team_lead": {"name": "Ubaid", "email": "ubaid@arbisoft.com"}}""").RootElement.Clone()]);

        var (status, body) = await GetAsync(await _host.GetTokenAsync());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("count").GetInt32());
        var project = body.GetProperty("projects")[0];
        Assert.Equal("7", project.GetProperty("id").GetString());
        Assert.Equal("ubaid@arbisoft.com", project.GetProperty("team_lead").GetProperty("email").GetString());
        Assert.Equal("DevLake", project.GetProperty("source").GetProperty("name").GetString());
    }

    [Fact]
    public async Task RequiresTheProjectsFetchScope()
    {
        _host.GrantScopes("stats:read");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(await _host.GetTokenAsync())).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(null)).Status);
        await _host.Workstream.DidNotReceiveWithAnyArgs().IsTokenValidAsync(default);
    }

    [Fact]
    public async Task ReturnsAClearErrorWhenWorkstreamRejectsTheToken()
    {
        _host.Workstream.IsTokenValidAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (status, body) = await GetAsync(await _host.GetTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.True(body.GetProperty("workstream_token_rejected").GetBoolean());
        Assert.Contains("rejected", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ReturnsAnErrorRatherThanAPartialList()
    {
        _host.Workstream.GetAllProjectsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalApiException("Workstream", "/projects returned 503.", HttpStatusCode.ServiceUnavailable));

        var (status, body) = await GetAsync(await _host.GetTokenAsync());

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.False(body.GetProperty("workstream_token_rejected").GetBoolean());
        Assert.False(body.TryGetProperty("projects", out _));
    }
}
