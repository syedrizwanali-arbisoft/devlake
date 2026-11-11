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
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

/// <summary>POST /auth/token/ end to end: real routing, form binding, MySQL and signing keys.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class AuthEndpointTests : IAsyncLifetime, IDisposable
{
    private const string Secret = ApiSeed.Secret;

    private readonly MySqlDatabaseFixture _database;
    private ApiFactory? _factory;
    private HttpClient? _http;
    private ApiSeed? _seed;

    public AuthEndpointTests(MySqlDatabaseFixture database)
    {
        _database = database;
    }

    public async Task InitializeAsync()
    {
        if (_database.Services is null)
        {
            return;
        }

        _factory = new ApiFactory(_database.DatabaseConnectionString);
        _http = _factory.CreateClient();
        _seed = new ApiSeed(_factory);
        await _seed.SeedSigningKeyAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _http?.Dispose();
        _factory?.Dispose();
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string? credentials, string? grantType = "client_credentials")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (credentials is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }

        var form = grantType is null ? [] : new Dictionary<string, string> { ["grant_type"] = grantType };
        request.Content = new FormUrlEncodedContent(form);
        return await _http!.SendAsync(request);
    }

    [MySqlFact]
    public async Task Token_GoodCredentials_ReturnsAVerifiableTokenAndRecordsIt()
    {
        var (clientId, clientRefId, secretId) = await _seed!.SeedClientAsync();

        var response = await PostAsync("/auth/token/", $"{clientId}:{Secret}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());
        Assert.Equal("projects:fetch stats:read", body.GetProperty("scope").GetString());

        var accessToken = body.GetProperty("access_token").GetString()!;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_seed!.KeyPair.PublicKeyPem);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = SecurityTestData.Issuer,
            ValidAudience = SecurityTestData.Audience,
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = _seed.Kid }
        });
        Assert.True(validation.IsValid);
        var jwt = (JsonWebToken)validation.SecurityToken;
        Assert.Equal(_seed.Kid, jwt.Kid);
        Assert.Equal(clientId, jwt.GetClaim("client_id").Value);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IAccessTokenRepository>().GetByJtiAsync(jwt.Id, CancellationToken.None);
        Assert.Equal(clientRefId, stored!.ClientRefId);
        Assert.Equal(secretId, stored.ClientSecretId);
        Assert.Equal("projects:fetch stats:read", stored.GrantedScopes);
        Assert.Equal("unknown", stored.IssuedToIp);
        Assert.Null(stored.RevokedOn);

        var secrets = await scope.ServiceProvider.GetRequiredService<IClientRepository>().GetSecretsAsync(clientRefId, CancellationToken.None);
        Assert.NotNull(secrets.Single().LastUsedOn);
    }

    [MySqlFact]
    public async Task Token_RouteWorksWithAndWithoutTheTrailingSlash()
    {
        var (clientId, _, _) = await _seed!.SeedClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync("/auth/token", $"{clientId}:{Secret}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync("/auth/token/", $"{clientId}:{Secret}")).StatusCode);
    }

    [MySqlFact]
    public async Task Token_ClientWithNoScopes_GetsAnEmptyScope()
    {
        var (clientId, _, _) = await _seed!.SeedClientAsync(scopes: []);

        var body = await (await PostAsync("/auth/token/", $"{clientId}:{Secret}")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(string.Empty, body.GetProperty("scope").GetString());
    }

    [MySqlFact]
    public async Task Token_WrongSecret_UnknownClientAndInactiveClient_AllGetTheSame401()
    {
        var (active, _, _) = await _seed!.SeedClientAsync();
        var (inactive, _, _) = await _seed!.SeedClientAsync(active: false);

        var wrongSecret = await PostAsync("/auth/token/", $"{active}:nope");
        var unknown = await PostAsync("/auth/token/", $"nobody-{Guid.NewGuid():N}:{Secret}");
        var disabled = await PostAsync("/auth/token/", $"{inactive}:{Secret}");

        foreach (var response in new[] { wrongSecret, unknown, disabled })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Basic", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            Assert.Equal("invalid_client", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }

        Assert.Equal(await wrongSecret.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [MySqlFact]
    public async Task Token_RevokedSecret_IsRefused()
    {
        var (clientId, _, secretId) = await _seed!.SeedClientAsync();
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IClientRepository>().RevokeSecretAsync(secretId, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/auth/token/", $"{clientId}:{Secret}")).StatusCode);
    }

    [MySqlFact]
    public async Task Token_MissingCredentialsOrBadGrant_IsRejectedBeforeAnyLookup()
    {
        var (clientId, _, _) = await _seed!.SeedClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/auth/token/", credentials: null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync("/auth/token/", $"{clientId}:{Secret}", grantType: null)).StatusCode);
        var unsupported = await PostAsync("/auth/token/", $"{clientId}:{Secret}", grantType: "password");
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal("unsupported_grant_type", (await unsupported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [MySqlFact]
    public async Task Token_NonFormContentType_Returns415()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/") { Content = JsonContent.Create(new { grant_type = "client_credentials" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("a:b"u8.ToArray()));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await _http!.SendAsync(request)).StatusCode);
    }

    [MySqlFact]
    public async Task Token_WithNoActiveSigningKey_IsAnErrorNotAToken()
    {
        var (clientId, _, _) = await _seed!.SeedClientAsync();
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<ISigningKeyRepository>();
            await keys.RetireAsync(_seed!.SigningKeyId, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        var response = await PostAsync("/auth/token/", $"{clientId}:{Secret}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
