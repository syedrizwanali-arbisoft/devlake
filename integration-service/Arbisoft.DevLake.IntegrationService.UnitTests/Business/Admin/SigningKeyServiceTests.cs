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

using System.Security.Cryptography;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class SigningKeyServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly ISigningKeyRepository _keys = Substitute.For<ISigningKeyRepository>();
    private readonly ISecretStore _secretStore = Substitute.For<ISecretStore>();
    private readonly SigningKeyService _service;
    private readonly List<string> _calls = [];

    public SigningKeyServiceTests()
    {
        _secretStore.SaveSecretAsync(default!, default!, default!).ReturnsForAnyArgs(_ => { _calls.Add("save-secret"); return Task.CompletedTask; });
        _keys.AddAsync(Arg.Any<SigningKey>(), Arg.Any<CancellationToken>()).Returns(_ => { _calls.Add("add-key"); return 8L; });
        _service = new SigningKeyService(_keys, _secretStore, new FakeTimeProvider(new DateTimeOffset(Now)));
    }

    [Fact]
    public async Task Rotate_StoresThePrivateKeyFirstThenActivatesAndRetiresTheOldKey()
    {
        _keys.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(new SigningKey { Id = 3 });
        string? privatePem = null;
        string? reference = null;
        _secretStore.SaveSecretAsync(Arg.Do<string>(r => reference = r), Arg.Do<string>(v => privatePem = v), "admin", Arg.Any<CancellationToken>())
            .Returns(_ => { _calls.Add("save-secret"); return Task.CompletedTask; });
        SigningKey? added = null;
        _keys.AddAsync(Arg.Do<SigningKey>(k => added = k), Arg.Any<CancellationToken>()).Returns(_ => { _calls.Add("add-key"); return 8L; });

        var result = await _service.RotateAsync("admin");

        Assert.True(result.Succeeded);
        Assert.Equal(["save-secret", "add-key"], _calls);
        Assert.Equal(reference, added!.PrivateKeyRef);
        Assert.Equal("RS256", added.Algorithm);
        Assert.Equal(Now, added.ActivatedOn);
        Assert.StartsWith("key-20261110090000-", added.Kid);
        Assert.DoesNotContain("PRIVATE", added.PublicKey);

        // The stored private key matches the public key in the row.
        using var privateKey = RSA.Create();
        privateKey.ImportFromPem(privatePem);
        using var publicKey = RSA.Create();
        publicKey.ImportFromPem(added.PublicKey);
        var data = "check"u8.ToArray();
        Assert.True(publicKey.VerifyData(data, privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        await _keys.Received(1).RetireAsync(3, Now, Arg.Any<CancellationToken>());
        Assert.Equal("Active", result.Value!.Status);
    }

    [Fact]
    public async Task Rotate_CreatesTheFirstKeyWhenThereIsNone()
    {
        Assert.True((await _service.RotateAsync("admin")).Succeeded);
        await _keys.DidNotReceiveWithAnyArgs().RetireAsync(default, default, default);
    }

    [Fact]
    public async Task List_MarksActiveRetiredAndPendingKeys()
    {
        _keys.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(new SigningKey { Id = 2 });
        _keys.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new SigningKey { Id = 3, ActivatedOn = Now.AddDays(1) },
            new SigningKey { Id = 2, ActivatedOn = Now.AddDays(-1) },
            new SigningKey { Id = 1, ActivatedOn = Now.AddDays(-9), RetiredOn = Now.AddDays(-1) }]);

        Assert.Equal(["Pending", "Active", "Retired"], (await _service.ListAsync()).Select(k => k.Status));
    }
}
