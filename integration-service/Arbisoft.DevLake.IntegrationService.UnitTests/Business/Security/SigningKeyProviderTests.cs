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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public sealed class SigningKeyProviderTests : IDisposable
{
    private readonly ISigningKeyRepository _keys = Substitute.For<ISigningKeyRepository>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _cache.Dispose();
    }

    private SigningKeyProvider Create() => new(_keys, _secrets, _cache, _time);

    private static (SigningKey Row, RsaKeyPair Pair) NewKey(long id, string kid)
    {
        var pair = RsaKeyPair.Generate();
        var row = new SigningKey { Id = id, Kid = kid, Algorithm = RsaKeyPair.Algorithm, PublicKey = pair.PublicKeyPem, PrivateKeyRef = $"ref-{kid}" };
        return (row, pair);
    }

    [Fact]
    public async Task GetActiveKey_ReturnsAKeyThatCanSign()
    {
        var (row, pair) = NewKey(7, "kid-a");
        _keys.GetActiveAsync(_time.GetUtcNow(), Arg.Any<CancellationToken>()).Returns(row);
        _secrets.GetAsync("ref-kid-a", Arg.Any<CancellationToken>()).Returns(pair.PrivateKeyPem);

        var active = await Create().GetActiveKeyAsync(CancellationToken.None);

        Assert.Equal(7, active.Id);
        Assert.Equal("kid-a", active.Kid);
        Assert.Equal("kid-a", active.Key.KeyId);
        Assert.True(((RsaSecurityKey)active.Key).PrivateKeyStatus == PrivateKeyStatus.Exists);
    }

    [Fact]
    public async Task GetActiveKey_WithNoActiveKey_Throws()
    {
        await Assert.ThrowsAsync<SigningKeyUnavailableException>(() => Create().GetActiveKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetActiveKey_WhenThePrivateKeyIsMissingFromTheStore_Throws()
    {
        var (row, _) = NewKey(1, "kid-a");
        _keys.GetActiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(row);
        _secrets.GetAsync("ref-kid-a", Arg.Any<CancellationToken>()).Returns((string?)null);

        var exception = await Assert.ThrowsAsync<SigningKeyUnavailableException>(() => Create().GetActiveKeyAsync(CancellationToken.None));

        Assert.Contains("kid-a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVerificationKey_ReturnsOnlyThePublicHalf_AndCachesIt()
    {
        var (row, _) = NewKey(1, "kid-a");
        _keys.GetByKidAsync("kid-a", Arg.Any<CancellationToken>()).Returns(row);
        var provider = Create();

        var first = (RsaSecurityKey)(await provider.GetVerificationKeyAsync("kid-a", CancellationToken.None))!;
        var second = await provider.GetVerificationKeyAsync("kid-a", CancellationToken.None);

        Assert.Equal(PrivateKeyStatus.DoesNotExist, first.PrivateKeyStatus);
        Assert.Same(first, second);
        await _keys.Received(1).GetByKidAsync("kid-a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVerificationKey_UnknownKid_ReturnsNull()
    {
        Assert.Null(await Create().GetVerificationKeyAsync("nope", CancellationToken.None));
    }
}
