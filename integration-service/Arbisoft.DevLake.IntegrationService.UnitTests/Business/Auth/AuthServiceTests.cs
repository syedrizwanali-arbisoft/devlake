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
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Auth;

public class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IAccessTokenRepository _accessTokens = Substitute.For<IAccessTokenRepository>();
    private readonly IClientSecretHasher _hasher = Substitute.For<IClientSecretHasher>();
    private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
    private readonly IDbSession _session = Substitute.For<IDbSession>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(call => call.ArgAt<string>(1) == "hash-of-" + call.ArgAt<string>(0));
        _hasher.Hash(Arg.Any<string>()).Returns(call => "hash-of-" + call.Arg<string>());
        _tokenGenerator.GenerateAsync(Arg.Any<TokenSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedToken("jwt-value", "jti-1", 9, Now, Now.AddSeconds(900)));
        _clients.GetScopeNamesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(["projects:fetch", "stats:read"]);
        _service = new AuthService(_clients, _accessTokens, _hasher, _tokenGenerator, _session, _time, NullLogger<AuthService>.Instance);
    }

    private static TokenRequest Request(string secret = "good") => new("sophia", secret, "203.0.113.7");

    private void GivenClient(bool isActive = true, params ClientSecret[] secrets)
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>())
            .Returns(new Client { Id = 1, ClientId = "sophia", IsActive = isActive, AccessTokenLifetimeSeconds = 900 });
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns(secrets);
    }

    private static ClientSecret Secret(long id, string plain, DateTimeOffset? expiresOn = null, DateTimeOffset? revokedOn = null) =>
        new() { Id = id, ClientRefId = 1, Secret = "hash-of-" + plain, ExpiresOn = expiresOn, RevokedOn = revokedOn };

    [Fact]
    public async Task Authenticate_GoodCredentials_ReturnsTheTokenResponse()
    {
        GivenClient(true, Secret(5, "good"));

        var result = await _service.AuthenticateAsync(Request(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("jwt-value", result.Token!.AccessToken);
        Assert.Equal("Bearer", result.Token.TokenType);
        Assert.Equal(900, result.Token.ExpiresIn);
        Assert.Equal("projects:fetch stats:read", result.Token.Scope);
        await _tokenGenerator.Received(1).GenerateAsync(
            Arg.Is<TokenSpecification>(spec => spec.ClientId == "sophia" && spec.LifetimeSeconds == 900 && spec.Scopes.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authenticate_Success_RecordsTheTokenAndSecretUseInOneTransaction()
    {
        GivenClient(true, Secret(5, "good"));

        await _service.AuthenticateAsync(Request(), CancellationToken.None);

        Received.InOrder(() =>
        {
            _session.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _accessTokens.InsertAsync(Arg.Any<AccessToken>(), Arg.Any<CancellationToken>());
            _clients.MarkSecretUsedAsync(5, Now, Arg.Any<CancellationToken>());
            _session.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _accessTokens.Received(1).InsertAsync(
            Arg.Is<AccessToken>(t => t.Jti == "jti-1" && t.ClientRefId == 1 && t.ClientSecretId == 5 && t.SigningKeyId == 9
                && t.GrantedScopes == "projects:fetch stats:read" && t.IssuedOn == Now && t.ExpiresOn == Now.AddSeconds(900) && t.IssuedToIp == "203.0.113.7"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authenticate_UnknownClient_IsRefused_AndStillSpendsAHashToHideWhichPartWasWrong()
    {
        var result = await _service.AuthenticateAsync(Request(), CancellationToken.None);

        Assert.Equal(AuthFailureReason.UnknownClient, result.Failure);
        _hasher.Received(1).Verify("good", Arg.Any<string>());
        await _tokenGenerator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default);
    }

    [Fact]
    public async Task Authenticate_WrongSecret_IsRefused_WithoutIssuingOrRecordingAnything()
    {
        GivenClient(true, Secret(5, "good"));

        var result = await _service.AuthenticateAsync(Request("bad"), CancellationToken.None);

        Assert.Equal(AuthFailureReason.InvalidSecret, result.Failure);
        await _tokenGenerator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default);
        await _session.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default);
    }

    [Fact]
    public async Task Authenticate_InactiveClient_IsRefusedEvenWithTheRightSecret()
    {
        GivenClient(false, Secret(5, "good"));

        var result = await _service.AuthenticateAsync(Request(), CancellationToken.None);

        Assert.Equal(AuthFailureReason.InactiveClient, result.Failure);
        await _tokenGenerator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default);
    }

    [Fact]
    public async Task Authenticate_RevokedOrExpiredSecret_IsRefused()
    {
        GivenClient(true, Secret(5, "good", revokedOn: Now.AddDays(-1)));
        Assert.Equal(AuthFailureReason.InvalidSecret, (await _service.AuthenticateAsync(Request(), CancellationToken.None)).Failure);

        GivenClient(true, Secret(6, "good", expiresOn: Now.AddSeconds(-1)));
        Assert.Equal(AuthFailureReason.InvalidSecret, (await _service.AuthenticateAsync(Request(), CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task Authenticate_DuringRotation_EitherLiveSecretWorks_AndTheRecordNamesTheOneUsed()
    {
        GivenClient(true, Secret(5, "old"), Secret(6, "new", expiresOn: Now.AddDays(30)));

        var viaNew = await _service.AuthenticateAsync(Request("new"), CancellationToken.None);
        var viaOld = await _service.AuthenticateAsync(Request("old"), CancellationToken.None);

        Assert.True(viaNew.Succeeded);
        Assert.True(viaOld.Succeeded);
        await _clients.Received(1).MarkSecretUsedAsync(6, Now, Arg.Any<CancellationToken>());
        await _clients.Received(1).MarkSecretUsedAsync(5, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authenticate_WhenNoSigningKeyIsAvailable_FailsLoudlyAndRecordsNothing()
    {
        GivenClient(true, Secret(5, "good"));
        _tokenGenerator.GenerateAsync(Arg.Any<TokenSpecification>(), Arg.Any<CancellationToken>())
            .Returns<IssuedToken>(_ => throw new SigningKeyUnavailableException("no key"));

        await Assert.ThrowsAsync<SigningKeyUnavailableException>(() => _service.AuthenticateAsync(Request(), CancellationToken.None));

        await _session.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default);
    }
}
