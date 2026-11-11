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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class SecretStoreTests
{
    private readonly ISecretStoreRepository _repository = Substitute.For<ISecretStoreRepository>();
    private readonly SecretEncryptor _encryptor = new(SecurityTestData.Options());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

    private SecretStore Create(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return new SecretStore(_repository, _encryptor, configuration, _time);
    }

    [Fact]
    public async Task Get_ConfiguredValueWinsOverTheDatabase()
    {
        _repository.GetEncryptedValueAsync("key-1", Arg.Any<CancellationToken>()).Returns(_encryptor.Encrypt("from-database"));

        var value = await Create(new() { ["Secrets:key-1"] = "from-environment" }).GetAsync("key-1", CancellationToken.None);

        Assert.Equal("from-environment", value);
        await _repository.DidNotReceiveWithAnyArgs().GetEncryptedValueAsync(default!, default);
    }

    [Fact]
    public async Task Get_FallsBackToTheDatabaseAndDecrypts()
    {
        _repository.GetEncryptedValueAsync("key-1", Arg.Any<CancellationToken>()).Returns(_encryptor.Encrypt("from-database"));

        Assert.Equal("from-database", await Create().GetAsync("key-1", CancellationToken.None));
    }

    [Fact]
    public async Task Get_UnknownReference_ReturnsNull()
    {
        _repository.GetEncryptedValueAsync("missing", Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.Null(await Create().GetAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task Save_StoresAnEncryptedValueWithTheCurrentTime()
    {
        await Create().SaveAsync("key-1", "private-pem", CancellationToken.None);

        await _repository.Received(1).SaveEncryptedValueAsync(
            "key-1",
            Arg.Is<string>(stored => stored != "private-pem" && _encryptor.Decrypt(stored) == "private-pem"),
            _time.GetUtcNow(),
            Arg.Any<CancellationToken>());
    }
}
