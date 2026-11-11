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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class ClientServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IScopeRepository _scopes = Substitute.For<IScopeRepository>();
    private readonly IClientSecretHasher _hasher = Substitute.For<IClientSecretHasher>();
    private readonly IDbSession _session = Substitute.For<IDbSession>();
    private readonly ClientService _service;

    public ClientServiceTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns(call => "hash-of-" + call.Arg<string>());
        _scopes.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Scope>([new Scope { Id = 1, Name = "stats:read" }, new Scope { Id = 2, Name = "projects:fetch" }], 2, 1, 200));
        _service = new ClientService(_clients, _scopes, _hasher, _session, new FakeTimeProvider(Now));
    }

    private static ClientInput Input(string clientId = "sophia", string name = "Sophia", bool active = true, int lifetime = 900) => new(clientId, name, active, lifetime);

    private void GivenClient(long id = 1) =>
        _clients.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new Client
        {
            Id = id,
            ClientId = "sophia",
            Name = "Sophia",
            IsActive = true,
            AccessTokenLifetimeSeconds = 900,
            CreatedOn = Now.AddDays(-5),
            CreatedBy = "first"
        });

    [Fact]
    public async Task Create_AValidClient_RecordsWhoCreatedIt()
    {
        _clients.InsertAsync(Arg.Any<Client>(), Arg.Any<CancellationToken>()).Returns(3L);

        var result = await _service.CreateAsync(Input(name: " Sophia "), "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value);
        await _clients.Received(1).InsertAsync(
            Arg.Is<Client>(c => c.ClientId == "sophia" && c.Name == "Sophia" && c.CreatedBy == "admin" && c.CreatedOn == Now && c.AccessTokenLifetimeSeconds == 900),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ab", "Name", 900)]
    [InlineData("Upper", "Name", 900)]
    [InlineData("has space", "Name", 900)]
    [InlineData("sophia", " ", 900)]
    [InlineData("sophia", "Name", 59)]
    [InlineData("sophia", "Name", 86_401)]
    public async Task Create_InvalidInput_IsRefusedWithoutAWrite(string clientId, string name, int lifetime)
    {
        var result = await _service.CreateAsync(Input(clientId, name, true, lifetime), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _clients.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Create_AClientIdThatExists_IsRefused()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new Client { Id = 1, ClientId = "sophia" });

        var result = await _service.CreateAsync(Input(), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _clients.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Update_ChangesTheEditableFields_AndRecordsWhoModifiedIt()
    {
        GivenClient();

        var result = await _service.UpdateAsync(1, Input(clientId: "ignored", name: "Renamed", active: false, lifetime: 1_800), "editor", CancellationToken.None);

        Assert.True(result.Succeeded);
        await _clients.Received(1).UpdateAsync(
            Arg.Is<Client>(c => c.ClientId == "sophia" && c.Name == "Renamed" && !c.IsActive && c.AccessTokenLifetimeSeconds == 1_800
                && c.ModifiedBy == "editor" && c.ModifiedOn == Now && c.CreatedBy == "first"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AnUnknownClient_Fails()
    {
        var result = await _service.UpdateAsync(9, Input(), "editor", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateSecret_StoresOnlyTheHash_AndReturnsThePlainValueOnce()
    {
        GivenClient();
        _clients.InsertSecretAsync(Arg.Any<ClientSecret>(), Arg.Any<CancellationToken>()).Returns(11L);

        var result = await _service.CreateSecretAsync(1, 90, "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        var created = result.Value!;
        Assert.Equal(11, created.SecretId);
        Assert.Equal("sophia", created.ClientId);
        Assert.Equal(Now.AddDays(90), created.ExpiresOn);
        Assert.True(created.Secret.Length >= 40);
        Assert.DoesNotContain('+', created.Secret);
        Assert.DoesNotContain('/', created.Secret);
        await _clients.Received(1).InsertSecretAsync(
            Arg.Is<ClientSecret>(s => s.Secret == "hash-of-" + created.Secret && s.ClientRefId == 1 && s.CreatedBy == "admin" && s.ExpiresOn == Now.AddDays(90)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSecret_TwiceGivesDifferentSecrets()
    {
        GivenClient();

        var first = await _service.CreateSecretAsync(1, 30, "admin", CancellationToken.None);
        var second = await _service.CreateSecretAsync(1, 30, "admin", CancellationToken.None);

        Assert.NotEqual(first.Value!.Secret, second.Value!.Secret);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ClientService.MaxSecretDays + 1)]
    public async Task CreateSecret_ALifetimeOutOfRange_IsRefused(int days)
    {
        GivenClient();

        var result = await _service.CreateSecretAsync(1, days, "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _clients.DidNotReceiveWithAnyArgs().InsertSecretAsync(default!, default);
    }

    [Fact]
    public async Task CreateSecret_ForAnUnknownClient_Fails()
    {
        var result = await _service.CreateSecretAsync(9, 30, "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RevokeSecret_ASecretOfAnotherClient_IsRefused()
    {
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns([new ClientSecret { Id = 5, ClientRefId = 1 }]);

        var result = await _service.RevokeSecretAsync(1, 6, CancellationToken.None);

        Assert.False(result.Succeeded);
        await _clients.DidNotReceiveWithAnyArgs().RevokeSecretAsync(default, default, default);
    }

    [Fact]
    public async Task RevokeSecret_AnAlreadyRevokedSecret_IsRefused()
    {
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns([new ClientSecret { Id = 5, ClientRefId = 1, RevokedOn = Now.AddDays(-1) }]);

        var result = await _service.RevokeSecretAsync(1, 5, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RevokeSecret_AnActiveSecret_IsRevokedNow()
    {
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns([new ClientSecret { Id = 5, ClientRefId = 1 }]);
        _clients.RevokeSecretAsync(5, Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.RevokeSecretAsync(1, 5, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SetScopes_ReplacesTheScopesInATransaction_AndRecordsWhoChangedThem()
    {
        GivenClient();

        var result = await _service.SetScopesAsync(1, [1, 2, 2], "editor", CancellationToken.None);

        Assert.True(result.Succeeded);
        Received.InOrder(() =>
        {
            _session.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _clients.ReplaceScopesAsync(1, Arg.Is<IReadOnlyCollection<long>>(ids => ids.Count == 2), "editor", Now, Arg.Any<CancellationToken>());
            _clients.UpdateAsync(Arg.Is<Client>(c => c.ModifiedBy == "editor"), Arg.Any<CancellationToken>());
            _session.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SetScopes_AnUnknownScope_ChangesNothing()
    {
        GivenClient();

        var result = await _service.SetScopesAsync(1, [1, 99], "editor", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _session.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default);
    }

    [Fact]
    public async Task SetScopes_AFailureRollsBackAndIsRethrown()
    {
        GivenClient();
        _clients.ReplaceScopesAsync(default, default!, default!, default, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("db down")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetScopesAsync(1, [1], "editor", CancellationToken.None));

        await _session.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _session.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Get_ShowsSecretStatusesAndTheAssignedScopes()
    {
        GivenClient();
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns(
        [
            new ClientSecret { Id = 1, ExpiresOn = Now.AddDays(5) },
            new ClientSecret { Id = 2, ExpiresOn = Now.AddDays(-1) },
            new ClientSecret { Id = 3, ExpiresOn = Now.AddDays(5), RevokedOn = Now.AddHours(-1) }
        ]);
        _clients.GetClientScopesAsync(1, Arg.Any<CancellationToken>()).Returns([new ClientScope { ClientRefId = 1, ScopeId = 2 }]);

        ClientDetailsDto? details = await _service.GetAsync(1, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(["Active", "Expired", "Revoked"], details.Secrets.Select(s => s.Status));
        Assert.Equal("projects:fetch", Assert.Single(details.AssignedScopes).Name);
        Assert.Equal(2, details.AllScopes.Count);
        Assert.DoesNotContain(details.Secrets, s => s.GetType().GetProperty("Secret") is not null);
    }

    [Fact]
    public async Task Get_AnUnknownClient_IsNull()
    {
        Assert.Null(await _service.GetAsync(9, CancellationToken.None));
    }
}
