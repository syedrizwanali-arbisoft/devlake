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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Auth;

public class AuthServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IScopeRepository _scopes = Substitute.For<IScopeRepository>();
    private readonly IAccessTokenRepository _accessTokens = Substitute.For<IAccessTokenRepository>();
    private readonly ISigningKeyProvider _signingKeys = Substitute.For<ISigningKeyProvider>();
    private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
    private readonly ISecretHasher _hasher = Substitute.For<ISecretHasher>();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _service = new AuthService(_clients, _scopes, _accessTokens, _signingKeys, _tokenGenerator, _hasher, new FakeTimeProvider(Now));

        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        _clients.GetUsableSecretsAsync(1, Now, Arg.Any<CancellationToken>()).Returns(
            [new ClientSecret { Id = 10, Secret = "old-hash" }, new ClientSecret { Id = 11, Secret = "new-hash" }]);
        _hasher.Verify("right", "new-hash").Returns(true);
        _scopes.GetNamesForClientAsync(1, Arg.Any<CancellationToken>()).Returns(["projects:fetch", "stats:read"]);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256);
        _signingKeys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new ActiveSigningKey(5, credentials));
        _tokenGenerator.Generate(default!, default!).ReturnsForAnyArgs("signed-token");
    }

    private static TokenRequest Request(string clientId = "sophia", string secret = "right", string? grant = "client_credentials") =>
        new(clientId, secret, grant, "10.0.0.1");

    [Fact]
    public async Task IssueToken_ReturnsTheContractResponse()
    {
        var result = await _service.IssueTokenAsync(Request());

        Assert.Equal(TokenError.None, result.Error);
        Assert.Equal(new TokenResponse("signed-token", "Bearer", 3600, "projects:fetch stats:read"), result.Response);
    }

    [Fact]
    public async Task IssueToken_UsesTheClientsLifetime()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().WithLifetime(60).Build());

        var result = await _service.IssueTokenAsync(Request());

        Assert.Equal(60, result.Response!.ExpiresIn);
        _tokenGenerator.Received(1).Generate(
            Arg.Is<TokenClaims>(c => c.ExpiresOn == Now.AddSeconds(60) && c.IssuedOn == Now), Arg.Any<SigningCredentials>());
    }

    [Fact]
    public async Task IssueToken_SavesTheTokenAndMarksTheMatchedSecretUsed()
    {
        await _service.IssueTokenAsync(Request());

        await _accessTokens.Received(1).AddAsync(Arg.Is<AccessToken>(t =>
            t.ClientRefId == 1
            && t.ClientSecretId == 11
            && t.SigningKeyId == 5
            && t.GrantedScopes == "projects:fetch stats:read"
            && t.IssuedOn == Now
            && t.ExpiresOn == Now.AddHours(1)
            && t.IssuedToIp == "10.0.0.1"
            && t.Jti != string.Empty), Arg.Any<CancellationToken>());
        await _clients.Received(1).UpdateSecretLastUsedAsync(11, Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("password")]
    [InlineData("CLIENT_CREDENTIALS")]
    public async Task IssueToken_RejectsOtherGrantTypes(string? grant)
    {
        var result = await _service.IssueTokenAsync(Request(grant: grant));

        Assert.Equal(TokenError.UnsupportedGrantType, result.Error);
    }

    [Fact]
    public async Task IssueToken_RejectsAnUnknownClient()
    {
        var result = await _service.IssueTokenAsync(Request(clientId: "nobody"));

        Assert.Equal(TokenError.InvalidClient, result.Error);
    }

    [Fact]
    public async Task IssueToken_RejectsAnInactiveClient()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Inactive().Build());

        var result = await _service.IssueTokenAsync(Request());

        Assert.Equal(TokenError.InvalidClient, result.Error);
        await _accessTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task IssueToken_RejectsAWrongSecret()
    {
        var result = await _service.IssueTokenAsync(Request(secret: "wrong"));

        Assert.Equal(TokenError.InvalidClient, result.Error);
        await _clients.DidNotReceiveWithAnyArgs().UpdateSecretLastUsedAsync(default, default, default);
    }

    [Fact]
    public async Task IssueToken_RejectsAClientWithNoUsableSecret()
    {
        _clients.GetUsableSecretsAsync(1, Now, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _service.IssueTokenAsync(Request());

        Assert.Equal(TokenError.InvalidClient, result.Error);
    }

    [Fact]
    public async Task IsTokenActive_IsFalseForUnknownAndRevokedTokens()
    {
        _accessTokens.GetByJtiAsync("active", Arg.Any<CancellationToken>()).Returns(new AccessToken());
        _accessTokens.GetByJtiAsync("revoked", Arg.Any<CancellationToken>()).Returns(new AccessToken { RevokedOn = Now });

        Assert.True(await _service.IsTokenActiveAsync("active"));
        Assert.False(await _service.IsTokenActiveAsync("revoked"));
        Assert.False(await _service.IsTokenActiveAsync("unknown"));
    }
}
