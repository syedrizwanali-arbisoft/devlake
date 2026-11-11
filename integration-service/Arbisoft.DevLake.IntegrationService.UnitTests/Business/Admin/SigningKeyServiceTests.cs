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
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class SigningKeyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly ISigningKeyRepository _keys = Substitute.For<ISigningKeyRepository>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly IDbSession _session = Substitute.For<IDbSession>();
    private readonly SigningKeyService _service;

    public SigningKeyServiceTests()
    {
        _keys.InsertAsync(Arg.Any<SigningKey>(), Arg.Any<CancellationToken>()).Returns(8L);
        _service = new SigningKeyService(_keys, _secrets, _session, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Rotate_SavesThePrivateKey_AddsTheNewKey_AndRetiresTheOldOneInOneTransaction()
    {
        _keys.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(new SigningKey { Id = 3, Kid = "old" });

        var result = await _service.RotateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        var key = result.Value!;
        Assert.Equal("Active", key.Status);
        Assert.Equal("RS256", key.Algorithm);
        Assert.Equal("signing-key-" + key.Kid, key.PrivateKeyRef);
        Assert.True(key.Kid.Length <= 100);
        Received.InOrder(() =>
        {
            _session.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _secrets.SaveAsync(key.PrivateKeyRef, Arg.Is<string>(pem => pem.Contains("PRIVATE KEY")), Arg.Any<CancellationToken>());
            _keys.InsertAsync(Arg.Is<SigningKey>(k => k.Kid == key.Kid && k.PublicKey.Contains("PUBLIC KEY") && k.ActivatedOn == Now), Arg.Any<CancellationToken>());
            _keys.RetireAsync(3, Now, Arg.Any<CancellationToken>());
            _session.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Rotate_TheVeryFirstKey_RetiresNothing()
    {
        var result = await _service.RotateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        await _keys.DidNotReceiveWithAnyArgs().RetireAsync(default, default, default);
    }

    [Fact]
    public async Task Rotate_APublishedPublicKeyNeverContainsThePrivateHalf()
    {
        await _service.RotateAsync(CancellationToken.None);

        await _keys.Received(1).InsertAsync(Arg.Is<SigningKey>(k => !k.PublicKey.Contains("PRIVATE")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rotate_AFailure_RollsBackAndIsRethrown()
    {
        _keys.InsertAsync(Arg.Any<SigningKey>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<long>(new InvalidOperationException("db down")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RotateAsync(CancellationToken.None));

        await _session.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _session.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task List_LabelsActiveRetiredAndPendingKeys()
    {
        _keys.GetActiveAsync(Now, Arg.Any<CancellationToken>()).Returns(new SigningKey { Id = 2 });
        _keys.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<SigningKey>(
        [
            new SigningKey { Id = 3, Kid = "next", ActivatedOn = Now.AddDays(1) },
            new SigningKey { Id = 2, Kid = "current", ActivatedOn = Now.AddDays(-1) },
            new SigningKey { Id = 1, Kid = "old", ActivatedOn = Now.AddDays(-9), RetiredOn = Now.AddDays(-1) }
        ], 3, 1, 200));

        var list = await _service.ListAsync(CancellationToken.None);

        Assert.Equal(["Pending", "Active", "Retired"], list.Select(k => k.Status));
    }
}
