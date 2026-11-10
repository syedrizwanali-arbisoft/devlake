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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class SecretStoreTests
{
    private readonly ISecretStoreRepository _repository = Substitute.For<ISecretStoreRepository>();
    private readonly ISecretEncryptor _encryptor = Substitute.For<ISecretEncryptor>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));

    private SecretStore Create(Dictionary<string, string?>? settings = null) => new(
        new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(), _repository, _encryptor, _time);

    [Fact]
    public async Task Get_PrefersConfiguration()
    {
        var store = Create(new() { ["Secrets:key-1"] = "from-env" });

        Assert.Equal("from-env", await store.GetSecretAsync("key-1"));
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }

    [Fact]
    public async Task Get_DecryptsAStoredSecret()
    {
        _repository.GetAsync("key-2", Arg.Any<CancellationToken>()).Returns(new SecretStoreEntry { ValueEncrypted = "v1:abc" });
        _encryptor.Decrypt("v1:abc").Returns("pem");

        Assert.Equal("pem", await Create().GetSecretAsync("key-2"));
    }

    [Fact]
    public async Task Get_ReturnsNullForAnUnknownReference()
    {
        _repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SecretStoreEntry?)null);

        Assert.Null(await Create().GetSecretAsync("nope"));
    }

    [Fact]
    public async Task Save_StoresOnlyTheEncryptedValue()
    {
        _encryptor.Encrypt("pem").Returns("v1:enc");

        await Create().SaveSecretAsync("key-3", "pem", "admin");

        await _repository.Received(1).AddAsync(Arg.Is<SecretStoreEntry>(e =>
            e.SecretRef == "key-3" && e.ValueEncrypted == "v1:enc" && e.CreatedBy == "admin"
            && e.CreatedOn == _time.GetUtcNow().UtcDateTime), Arg.Any<CancellationToken>());
    }
}
