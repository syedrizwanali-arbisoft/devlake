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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class SigningKeyProviderTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISigningKeyRepository _repository = Substitute.For<ISigningKeyRepository>();
    private readonly ISecretStore _secretStore = Substitute.For<ISecretStore>();
    private readonly SigningKeyProvider _provider;

    public SigningKeyProviderTests()
    {
        _provider = new SigningKeyProvider(_repository, _secretStore, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task GetActive_ReadsThePrivateKeyFromTheSecretStore()
    {
        var key = TestKey.Create(7, "key-1", Now.AddDays(-1));
        _repository.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(key.Row);
        _secretStore.GetSecret(key.Row.PrivateKeyRef).Returns(key.PrivatePem);

        var active = await _provider.GetActiveAsync();

        Assert.Equal(7, active.SigningKeyId);
        Assert.Equal("key-1", active.Credentials.Key.KeyId);
        Assert.Equal(SecurityAlgorithms.RsaSha256, active.Credentials.Algorithm);
    }

    [Fact]
    public async Task GetActive_FailsWithoutAnActiveKey()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.GetActiveAsync());

        Assert.Contains("no active signing key", exception.Message);
    }

    [Fact]
    public async Task GetActive_NamesTheMissingSecretReference()
    {
        var key = TestKey.Create(7, "key-1", Now.AddDays(-1));
        _repository.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(key.Row);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.GetActiveAsync());

        Assert.Contains(key.Row.PrivateKeyRef, exception.Message);
    }

    [Fact]
    public async Task GetActive_RejectsAnUnsupportedAlgorithm()
    {
        var key = TestKey.Create(7, "key-1", Now.AddDays(-1));
        key.Row.Algorithm = SecurityAlgorithms.HmacSha256;
        _repository.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(key.Row);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.GetActiveAsync());
    }

    [Fact]
    public async Task GetVerificationKeys_IncludesRetiredKeysAndNeverReadsPrivateKeys()
    {
        var retired = TestKey.Create(1, "old", Now.AddDays(-30));
        retired.Row.RetiredOn = Now.AddDays(-1);
        var active = TestKey.Create(2, "new", Now.AddDays(-1));
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([active.Row, retired.Row]);

        var keys = await _provider.GetVerificationKeysAsync();

        Assert.Equal(["new", "old"], keys.Select(k => k.KeyId));
        _secretStore.DidNotReceiveWithAnyArgs().GetSecret(default!);
    }
}
