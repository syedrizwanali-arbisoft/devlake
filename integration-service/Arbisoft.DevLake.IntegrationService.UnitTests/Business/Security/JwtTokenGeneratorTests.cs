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

using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public sealed class JwtTokenGeneratorTests : IDisposable
{
    private static readonly string[] Scopes = ["stats:read", "projects:fetch"];

    private readonly ISigningKeyRepository _keys = Substitute.For<ISigningKeyRepository>();
    private readonly Dictionary<string, string> _privateKeys = [];
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SigningKeyProvider _provider;
    private readonly JwtTokenGenerator _generator;

    public JwtTokenGeneratorTests()
    {
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _privateKeys.GetValueOrDefault(call.Arg<string>()));
        _provider = new SigningKeyProvider(_keys, secrets, _cache, _time);
        _generator = new JwtTokenGenerator(_provider, SecurityTestData.Options(), _time);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private SigningKey AddKey(long id, string kid)
    {
        var pair = RsaKeyPair.Generate();
        _privateKeys[$"ref-{kid}"] = pair.PrivateKeyPem;
        var row = new SigningKey { Id = id, Kid = kid, Algorithm = RsaKeyPair.Algorithm, PublicKey = pair.PublicKeyPem, PrivateKeyRef = $"ref-{kid}" };
        _keys.GetByKidAsync(kid, Arg.Any<CancellationToken>()).Returns(row);
        return row;
    }

    private async Task<TokenValidationResult> ValidateAsync(string token, string kid)
    {
        var key = await _provider.GetVerificationKeyAsync(kid, CancellationToken.None);
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = SecurityTestData.Issuer,
            ValidAudience = SecurityTestData.Audience,
            IssuerSigningKey = key,
            ValidateLifetime = false
        });
    }

    [Fact]
    public async Task Generate_ProducesASignedTokenWithKidJtiClientScopesAndExpiry()
    {
        var key5 = AddKey(5, "kid-a");
        _keys.GetActiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(key5);

        var issued = await _generator.GenerateAsync(new TokenSpecification("sophia", Scopes, 3600), CancellationToken.None);

        var result = await ValidateAsync(issued.AccessToken, "kid-a");
        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("kid-a", jwt.Kid);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal(issued.Jti, jwt.Id);
        Assert.Equal("sophia", jwt.GetClaim(TokenClaims.Client).Value);
        Assert.Equal("stats:read projects:fetch", jwt.GetClaim(TokenClaims.Scope).Value);
        Assert.Equal(_time.GetUtcNow().AddHours(1).ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds());
        Assert.Equal(5, issued.SigningKeyId);
        Assert.Equal(_time.GetUtcNow().AddHours(1), issued.ExpiresOn);
    }

    [Fact]
    public async Task Generate_GivesEveryTokenItsOwnJti()
    {
        var key5 = AddKey(5, "kid-a");
        _keys.GetActiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(key5);
        var specification = new TokenSpecification("sophia", Scopes, 60);

        var first = await _generator.GenerateAsync(specification, CancellationToken.None);
        var second = await _generator.GenerateAsync(specification, CancellationToken.None);

        Assert.NotEqual(first.Jti, second.Jti);
    }

    [Fact]
    public async Task Token_SignedBeforeARotation_StillVerifiesAfterIt()
    {
        var key1 = AddKey(1, "kid-old");
        _keys.GetActiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(key1);
        var before = await _generator.GenerateAsync(new TokenSpecification("sophia", Scopes, 3600), CancellationToken.None);

        var key2 = AddKey(2, "kid-new");
        _keys.GetActiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(key2);
        var after = await _generator.GenerateAsync(new TokenSpecification("sophia", Scopes, 3600), CancellationToken.None);

        Assert.True((await ValidateAsync(before.AccessToken, "kid-old")).IsValid);
        Assert.True((await ValidateAsync(after.AccessToken, "kid-new")).IsValid);
        Assert.Equal("kid-new", new JsonWebToken(after.AccessToken).Kid);
        Assert.False((await ValidateAsync(before.AccessToken, "kid-new")).IsValid);
    }

    [Fact]
    public async Task Generate_WithoutAnActiveKey_Throws()
    {
        await Assert.ThrowsAsync<SigningKeyUnavailableException>(() =>
            _generator.GenerateAsync(new TokenSpecification("sophia", Scopes, 60), CancellationToken.None));
    }
}
