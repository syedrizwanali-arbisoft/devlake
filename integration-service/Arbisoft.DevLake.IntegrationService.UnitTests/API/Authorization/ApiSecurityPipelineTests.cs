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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

/// <summary>Runs the real API pipeline, with only the repositories replaced, from token issue through to a protected call.</summary>
public sealed class ApiSecurityPipelineTests : IDisposable
{
    private const string ClientSecretValue = "the-client-secret";

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IScopeRepository _scopes = Substitute.For<IScopeRepository>();
    private readonly IAccessTokenRepository _accessTokens = Substitute.For<IAccessTokenRepository>();
    private readonly ISigningKeyRepository _signingKeys = Substitute.For<ISigningKeyRepository>();
    private readonly Dictionary<string, AccessToken> _savedTokens = [];
    private readonly TestKey _firstKey = TestKey.Create(1, "key-1", DateTime.UtcNow.AddDays(-30));
    private readonly TestKey _secondKey = TestKey.Create(2, "key-2", DateTime.UtcNow.AddDays(-1));
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public ApiSecurityPipelineTests()
    {
        var clientSecret = new ClientSecret { Id = 10, Secret = new SecretHasher().Hash(ClientSecretValue) };
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        _clients.GetUsableSecretsAsync(1, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([clientSecret]);
        _scopes.GetNamesForClientAsync(1, Arg.Any<CancellationToken>()).Returns(["stats:read"]);
        _signingKeys.GetActiveAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_firstKey.Row);
        _signingKeys.GetAllAsync(Arg.Any<CancellationToken>()).Returns([_firstKey.Row]);
        _accessTokens.AddAsync(Arg.Do<AccessToken>(t => _savedTokens[t.Jti] = t), Arg.Any<CancellationToken>()).Returns(1L);
        _accessTokens.GetByJtiAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _savedTokens.GetValueOrDefault(call.Arg<string>()));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Database:RunMigrationsOnStartup", "false");
            host.UseSetting("Secrets:key-1", _firstKey.PrivatePem);
            host.UseSetting("Secrets:key-2", _secondKey.PrivatePem);
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(_clients));
                services.Replace(ServiceDescriptor.Singleton(_scopes));
                services.Replace(ServiceDescriptor.Singleton(_accessTokens));
                services.Replace(ServiceDescriptor.Singleton(_signingKeys));
                services.AddControllers().AddApplicationPart(typeof(ProtectedTestController).Assembly);
            });
        });
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpResponseMessage> PostTokenAsync(string credentials, string body = "grant_type=client_credentials")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        return await _http.SendAsync(request);
    }

    private async Task<string> GetTokenAsync()
    {
        using var response = await PostTokenAsync("sophia:" + ClientSecretValue);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpStatusCode> CallAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _http.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task TokenEndpoint_ReturnsTheContractFields()
    {
        using var response = await PostTokenAsync("sophia:" + ClientSecretValue);

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
        using var response = await PostTokenAsync("sophia:wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenEndpoint_RejectsAnUnsupportedGrantWith400()
    {
        using var response = await PostTokenAsync("sophia:" + ClientSecretValue, "grant_type=password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedApi_AllowsATokenWithTheScope()
    {
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", await GetTokenAsync()));
    }

    [Fact]
    public async Task ProtectedApi_Returns403WhenTheScopeIsMissing()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync("/test/projects", await GetTokenAsync()));
    }

    [Fact]
    public async Task ProtectedApi_Returns401WithoutAToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", null));
    }

    [Fact]
    public async Task ProtectedApi_Returns401ForATamperedToken()
    {
        var token = await GetTokenAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", token[..^4] + "AAAA"));
    }

    [Fact]
    public async Task ProtectedApi_Returns401OnceTheTokenIsRevoked()
    {
        var token = await GetTokenAsync();
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", token));

        foreach (var saved in _savedTokens.Values)
        {
            saved.RevokedOn = DateTime.UtcNow;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", token));
    }

    [Fact]
    public async Task ProtectedApi_Returns401ForATokenThatWasNeverRecorded()
    {
        var token = await GetTokenAsync();
        _savedTokens.Clear();

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync("/test/stats", token));
    }

    private string SignDirectly(string issuer, string audience, DateTime issuedOn, DateTime expiresOn)
    {
        var jti = Guid.NewGuid().ToString();
        _savedTokens[jti] = new AccessToken { Jti = jti };

        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(_firstKey.PrivatePem);
        var parameters = rsa.ExportParameters(true);
        var credentials = new SigningCredentials(new RsaSecurityKey(parameters) { KeyId = _firstKey.Kid }, SecurityAlgorithms.RsaSha256);
        var generator = new JwtTokenGenerator(Options.Create(new JwtOptions { Issuer = issuer, Audience = audience }));
        return generator.Generate(new TokenClaims(jti, "sophia", ["stats:read"], issuedOn, expiresOn), credentials);
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
        var tokenFromFirstKey = await GetTokenAsync();
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromFirstKey));

        _firstKey.Row.RetiredOn = DateTime.UtcNow;
        _signingKeys.GetActiveAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_secondKey.Row);
        _signingKeys.GetAllAsync(Arg.Any<CancellationToken>()).Returns([_secondKey.Row, _firstKey.Row]);

        var tokenFromSecondKey = await GetTokenAsync();
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromFirstKey));

        // The cached key set doesn't hold key-2 yet. The first call asks for a refresh, and the next one succeeds.
        await CallAsync("/test/stats", tokenFromSecondKey);
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromSecondKey));
        Assert.Equal(HttpStatusCode.OK, await CallAsync("/test/stats", tokenFromFirstKey));
    }
}
