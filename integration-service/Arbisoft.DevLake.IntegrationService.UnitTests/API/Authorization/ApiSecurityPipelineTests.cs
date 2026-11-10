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
using System.Security.Cryptography;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Authorization;

[ApiController]
[Route("test")]
public sealed class ProtectedTestController : ControllerBase
{
    [HttpGet("stats")]
    [Authorize(Policy = ApiScopes.StatsRead)]
    public IActionResult Stats() => Ok();

    [HttpGet("projects")]
    [Authorize(Policy = ApiScopes.ProjectsFetch)]
    public IActionResult Projects() => Ok();
}

/// <summary>From token issue through to a protected call, on the real pipeline.</summary>
public sealed class ApiSecurityPipelineTests : IDisposable
{
    private readonly ApiTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<HttpStatusCode> CallAsync(string path, string? token)
    {
        using var response = await _host.SendAsync(HttpMethod.Get, path, token);
        return response.StatusCode;
    }

    private string SignDirectly(string issuer, string audience, DateTime issuedOn, DateTime expiresOn)
    {
        var jti = Guid.NewGuid().ToString();
        _host.SavedTokens[jti] = new AccessToken { Jti = jti };

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_host.FirstKey.PrivatePem);
        var credentials = new SigningCredentials(
            new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = _host.FirstKey.Kid }, SecurityAlgorithms.RsaSha256);
        var generator = new JwtTokenGenerator(Options.Create(new JwtOptions { Issuer = issuer, Audience = audience }));
        return generator.Generate(new TokenClaims(jti, "sophia", ["stats:read"], issuedOn, expiresOn), credentials);
    }

    [Fact]
    public async Task TokenEndpoint_ReturnsTheContractFields()
    {
        using var response = await _host.PostTokenAsync("sophia:" + ApiTestHost.ClientSecretValue);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", json.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(3600, json.RootElement.GetProperty("expires_in").GetInt32());
        Assert.Equal("stats:read", json.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task TokenEndpoint_RejectsAWrongSecretWith401()
    {
        using var response = await _host.PostTokenAsync("sophia:wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenEndpoint_RejectsAnUnsupportedGrantWith400()
    {
        using var response = await _host.PostTokenAsync("sophia:" + ApiTestHost.ClientSecretValue, "grant_type=password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedApi_AllowsATokenWithTheScope()
    {
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", await _host.GetTokenAsync()));
    }

    [Fact]
    public async Task ProtectedApi_Returns403WhenTheScopeIsMissing()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync("/test/projects", await _host.GetTokenAsync()));
    }

    [Fact]
    public async Task ProtectedApi_Returns401WithoutAToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", null));
    }

    [Fact]
    public async Task ProtectedApi_Returns401ForATamperedToken()
    {
        var token = await _host.GetTokenAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", token[..^4] + "AAAA"));
    }

    [Fact]
    public async Task ProtectedApi_Returns401OnceTheTokenIsRevoked()
    {
        var token = await _host.GetTokenAsync();
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", token));

        foreach (var saved in _host.SavedTokens.Values)
        {
            saved.RevokedOn = DateTime.UtcNow;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", token));
    }

    [Fact]
    public async Task ProtectedApi_Returns401ForATokenThatWasNeverRecorded()
    {
        var token = await _host.GetTokenAsync();
        _host.SavedTokens.Clear();

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", token));
    }

    [Fact]
    public async Task ProtectedApi_ChecksIssuerAudienceAndExpiry()
    {
        const string issuer = "arbisoft-devlake-integration-service";
        const string audience = "arbisoft-devlake-integration-api";
        var now = DateTime.UtcNow;

        // The valid token proves the three failures below come from the claim each one changes.
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", SignDirectly(issuer, audience, now, now.AddHours(1))));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", SignDirectly("other", audience, now, now.AddHours(1))));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", SignDirectly(issuer, "other", now, now.AddHours(1))));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", SignDirectly(issuer, audience, now.AddHours(-2), now.AddHours(-1))));
    }

    [Fact]
    public async Task KeyRotation_KeepsOlderTokensValidAndPicksUpTheNewKey()
    {
        var tokenFromFirstKey = await _host.GetTokenAsync();
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromFirstKey));

        _host.FirstKey.Row.RetiredOn = DateTime.UtcNow;
        _host.SigningKeys.GetActiveAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_host.SecondKey.Row);
        _host.SigningKeys.GetAllAsync(Arg.Any<CancellationToken>()).Returns([_host.SecondKey.Row, _host.FirstKey.Row]);

        var tokenFromSecondKey = await _host.GetTokenAsync();
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromFirstKey));

        // The cached key set doesn't hold key-2 yet. The first call asks for a refresh, and the next one succeeds.
        await CallAsync("/test/stats", tokenFromSecondKey);
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromSecondKey));
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromFirstKey));
    }
}
