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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class ClientServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IScopeRepository _scopes = Substitute.For<IScopeRepository>();
    private readonly SecretHasher _hasher = new();
    private readonly ClientService _service;

    public ClientServiceTests()
    {
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        _clients.GetByClientIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Client?)null);
        _clients.AddAsync(Arg.Any<Client>(), Arg.Any<CancellationToken>()).Returns(9L);
        _clients.AddSecretAsync(Arg.Any<ClientSecret>(), Arg.Any<CancellationToken>()).Returns(77L);
        _scopes.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new Scope { Id = 1, Name = "stats:read" }, new Scope { Id = 2, Name = "projects:fetch" }]);
        _scopes.GetNamesForClientAsync(1, Arg.Any<CancellationToken>()).Returns(["stats:read"]);
        _service = new ClientService(_clients, _scopes, _hasher, new FakeTimeProvider(new DateTimeOffset(Now)));
    }

    [Fact]
    public async Task Create_SavesTheClientWithTheAdminName()
    {
        var result = await _service.CreateAsync(new ClientInput(" sophia-stage ", " Sophia ", true, 3600), "admin");

        Assert.Equal(AdminResult<long>.Ok(9), result);
        await _clients.Received(1).AddAsync(Arg.Is<Client>(c => c.ClientId == "sophia-stage" && c.Name == "Sophia"
            && c.IsActive && c.AccessTokenLifetimeSeconds == 3600 && c.CreatedBy == "admin" && c.CreatedOn == Now), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ab", "Sophia", 3600, "Client ID")]
    [InlineData("Sophia", "Sophia", 3600, "Client ID")]
    [InlineData("has space", "Sophia", 3600, "Client ID")]
    [InlineData("sophia", " ", 3600, "name")]
    [InlineData("sophia", "Sophia", 59, "lifetime")]
    [InlineData("sophia", "Sophia", 86401, "lifetime")]
    public async Task Create_RejectsInvalidInput(string clientId, string name, int lifetime, string mentions)
    {
        var result = await _service.CreateAsync(new ClientInput(clientId, name, true, lifetime), "admin");

        Assert.False(result.Succeeded);
        Assert.Contains(mentions, result.Error, StringComparison.OrdinalIgnoreCase);
        await _clients.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Create_RejectsADuplicateClientId()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());

        var result = await _service.CreateAsync(new ClientInput("sophia", "Sophia", true, 3600), "admin");

        Assert.Contains("already exists", result.Error);
    }

    [Fact]
    public async Task Update_RecordsWhoChangedIt()
    {
        var result = await _service.UpdateAsync(1, new ClientInput("ignored", "Renamed", false, 600), "admin2");

        Assert.True(result.Succeeded);
        await _clients.Received(1).UpdateAsync(Arg.Is<Client>(c => c.ClientId == "sophia" && c.Name == "Renamed" && !c.IsActive
            && c.AccessTokenLifetimeSeconds == 600 && c.ModifiedBy == "admin2" && c.ModifiedOn == Now), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSecret_ReturnsThePlainValueOnceAndStoresAHash()
    {
        ClientSecret? stored = null;
        _clients.AddSecretAsync(Arg.Do<ClientSecret>(s => stored = s), Arg.Any<CancellationToken>()).Returns(77L);

        var result = await _service.CreateSecretAsync(1, 30, "admin");

        Assert.True(result.Succeeded);
        var secret = result.Value!;
        Assert.Equal(77, secret.SecretId);
        Assert.Equal("sophia", secret.ClientId);
        Assert.Equal(Now.AddDays(30), secret.ExpiresOn);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", secret.Secret);
        Assert.NotEqual(secret.Secret, stored!.Secret);
        Assert.True(_hasher.Verify(secret.Secret, stored.Secret));
        Assert.Equal("admin", stored.CreatedBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(731)]
    public async Task CreateSecret_RejectsAnOutOfRangeLifetime(int days)
    {
        Assert.False((await _service.CreateSecretAsync(1, days, "admin")).Succeeded);
    }

    [Fact]
    public async Task RevokeSecret_OnlyRevokesTheClientsOwnActiveSecret()
    {
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns([
            new ClientSecret { Id = 5, ClientRefId = 1 }, new ClientSecret { Id = 6, ClientRefId = 1, RevokedOn = Now }]);

        Assert.True((await _service.RevokeSecretAsync(1, 5)).Succeeded);
        Assert.Contains("already revoked", (await _service.RevokeSecretAsync(1, 6)).Error);
        Assert.Contains("not found", (await _service.RevokeSecretAsync(1, 99)).Error);
        await _clients.Received(1).RevokeSecretAsync(5, Now, Arg.Any<CancellationToken>());
        await _clients.DidNotReceive().RevokeSecretAsync(6, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetScopes_AddsAndRemovesOnlyTheDifference()
    {
        var result = await _service.SetScopesAsync(1, [2], "admin");

        Assert.True(result.Succeeded);
        await _scopes.Received(1).AssignToClientAsync(Arg.Is<ClientScope>(cs => cs.ClientRefId == 1 && cs.ScopeId == 2 && cs.CreatedBy == "admin"),
            Arg.Any<CancellationToken>());
        await _scopes.Received(1).RemoveFromClientAsync(1, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetScopes_RejectsAnUnknownScope()
    {
        Assert.False((await _service.SetScopesAsync(1, [42], "admin")).Succeeded);
        await _scopes.DidNotReceiveWithAnyArgs().AssignToClientAsync(default!, default);
    }

    [Fact]
    public async Task Get_MarksSecretStatusesAndAssignedScopes()
    {
        _clients.GetSecretsAsync(1, Arg.Any<CancellationToken>()).Returns([
            new ClientSecret { Id = 1, ExpiresOn = Now.AddDays(1) },
            new ClientSecret { Id = 2, ExpiresOn = Now.AddDays(-1) },
            new ClientSecret { Id = 3, ExpiresOn = Now.AddDays(1), RevokedOn = Now }]);

        var details = await _service.GetAsync(1);

        Assert.Equal(["Active", "Expired", "Revoked"], details!.Secrets.Select(s => s.Status));
        Assert.Equal(["stats:read"], details.AssignedScopes.Select(s => s.Name));
        Assert.Equal(2, details.AllScopes.Count);
    }
}
