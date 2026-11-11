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
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

/// <summary>Bearer authentication, scope checks and revocation, end to end through the real pipeline and database.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class ScopeAuthorizationEndpointTests : IAsyncLifetime, IDisposable
{
    private readonly MySqlDatabaseFixture _database;
    private ApiFactory? _factory;
    private HttpClient? _http;
    private ApiSeed? _seed;

    public ScopeAuthorizationEndpointTests(MySqlDatabaseFixture database)
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

    private async Task<HttpStatusCode> GetStatusAsync(string path, string? bearer)
    {
        using var response = await GetAsync(path, bearer);
        return response.StatusCode;
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await _http!.SendAsync(request);
    }

    [MySqlFact]
    public async Task NoToken_Gets401WithABearerChallengeAndAProblemBodyCarryingTheCorrelationId()
    {
        using var response = await GetAsync("/test/stats", bearer: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.GetProperty("correlationId").GetString());
    }

    [MySqlFact]
    public async Task ATokenWithTheRequiredScope_Gets200_AndOneWithoutItGets403()
    {
        var client = await _seed!.SeedClientAsync(scopes: ["stats:read"]);
        var token = await ApiSeed.RequestTokenAsync(_http!, client);

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/stats", token));
        using var forbidden = await GetAsync("/test/projects", token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(403, (await forbidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
    }

    [MySqlFact]
    public async Task ATokenWithBothScopes_ReachesBothEndpoints()
    {
        var token = await ApiSeed.RequestTokenAsync(_http!, await _seed!.SeedClientAsync());

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/stats", token));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/projects", token));
    }

    [MySqlFact]
    public async Task ARevokedToken_IsRejectedBeforeItExpires()
    {
        var client = await _seed!.SeedClientAsync();
        var token = await ApiSeed.RequestTokenAsync(_http!, client);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/stats", token));

        await _seed.RevokeTokenAsync(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).Id);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", token));
    }

    [MySqlFact]
    public async Task ATokenSignedBeforeAKeyRotation_StillWorks_AfterIt()
    {
        var client = await _seed!.SeedClientAsync();
        var beforeRotation = await ApiSeed.RequestTokenAsync(_http!, client);
        var oldKid = _seed.Kid;

        await _seed.RotateSigningKeyAsync();
        var afterRotation = await ApiSeed.RequestTokenAsync(_http!, client);

        Assert.NotEqual(oldKid, _seed.Kid);
        Assert.Equal(oldKid, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(beforeRotation).Kid);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/stats", beforeRotation));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/stats", afterRotation));
    }

    [MySqlFact]
    public async Task AControlToken_MadeByHand_IsAccepted_SoTheFailuresBelowAreDownToTheOneChange()
    {
        var client = await _seed!.SeedClientAsync();

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/stats", await _seed.MintAndRecordAsync(client)));
    }

    [MySqlFact]
    public async Task ATokenThatHasExpired_WrongIssuerOrWrongAudience_IsRejected()
    {
        var client = await _seed!.SeedClientAsync();

        var expired = await _seed.MintAndRecordAsync(client, new MintSpec(ExpiresOn: DateTimeOffset.UtcNow.AddMinutes(-10)));
        var wrongIssuer = await _seed.MintAndRecordAsync(client, new MintSpec(Issuer: "https://someone-else.example"));
        var wrongAudience = await _seed.MintAndRecordAsync(client, new MintSpec(Audience: "another-service"));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", expired));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", wrongIssuer));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", wrongAudience));
    }

    [MySqlFact]
    public async Task ATokenWithAnUnknownKid_OrSignedWithAnotherKey_OrAnotherAlgorithm_OrAlteredPayload_IsRejected()
    {
        var client = await _seed!.SeedClientAsync();
        var otherKey = new SymmetricSecurityKey(new byte[64]) { KeyId = _seed.Kid };

        var unknownKid = await _seed.MintAndRecordAsync(client, new MintSpec(Kid: "no-such-kid"));
        var hmacWithKnownKid = await _seed.MintAndRecordAsync(client, new MintSpec(SigningKey: otherKey, Algorithm: SecurityAlgorithms.HmacSha512));
        var good = await _seed.MintAndRecordAsync(client);
        var parts = good.Split('.');
        var altered = $"{parts[0]}.{parts[1][..^2]}AA.{parts[2]}";

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", unknownKid));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", hmacWithKnownKid));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", altered));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", "not-a-jwt"));
    }

    [MySqlFact]
    public async Task AValidlySignedTokenTheAuthApiNeverIssued_IsRejected()
    {
        var neverRecorded = _seed!.Mint(new MintSpec());

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/stats", neverRecorded));
    }

    [MySqlFact]
    public async Task AnEndpointWithNoAttribute_DeniesAnonymousCallers_ButAcceptsAnyValidToken()
    {
        var token = await ApiSeed.RequestTokenAsync(_http!, await _seed!.SeedClientAsync(scopes: []));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/test/unmarked", bearer: null));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/test/unmarked", token));
    }

    [MySqlFact]
    public async Task HealthEndpointsStayOpen()
    {
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/health/live", bearer: null));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/health/ready", bearer: null));
    }
}
