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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

[Collection(MySqlTestGroup.Name)]
public class ClientRepositoryTests : RepositoryTestBase
{
    public ClientRepositoryTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
    }

    private IClientRepository Repository => Get<IClientRepository>();

    [MySqlFact]
    public async Task Insert_ThenGetByClientId_RoundTripsEveryColumnAndUtcTimes()
    {
        var created = await InsertClientAsync("round-trip");

        var found = await Repository.GetByClientIdAsync("round-trip", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.True(found.IsActive);
        Assert.Equal(3600, found.AccessTokenLifetimeSeconds);
        Assert.Equal(Instant, found.CreatedOn);
        Assert.Equal(TimeSpan.Zero, found.CreatedOn.Offset);
        Assert.Null(found.ModifiedOn);
    }

    [MySqlFact]
    public async Task Insert_DuplicateClientId_IsRejectedByTheUniqueKey()
    {
        var existing = await InsertClientAsync();

        var exception = await Assert.ThrowsAsync<MySqlException>(() => InsertClientAsync(existing.ClientId));

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [MySqlFact]
    public async Task Update_ChangesTheEditableColumns()
    {
        var client = await InsertClientAsync();
        var changed = new Client
        {
            Id = client.Id,
            ClientId = client.ClientId,
            Name = "Renamed",
            IsActive = false,
            AccessTokenLifetimeSeconds = 120,
            CreatedOn = client.CreatedOn,
            CreatedBy = client.CreatedBy,
            ModifiedOn = Instant,
            ModifiedBy = "admin"
        };

        Assert.True(await Repository.UpdateAsync(changed, CancellationToken.None));

        var found = await Repository.GetByIdAsync(client.Id, CancellationToken.None);
        Assert.Equal("Renamed", found!.Name);
        Assert.False(found.IsActive);
        Assert.Equal(120, found.AccessTokenLifetimeSeconds);
        Assert.Equal("admin", found.ModifiedBy);
    }

    [MySqlFact]
    public async Task GetByClientId_Unknown_ReturnsNull()
    {
        Assert.Null(await Repository.GetByClientIdAsync("does-not-exist", CancellationToken.None));
    }

    [MySqlFact]
    public async Task List_ReturnsAPageAndTheTotal()
    {
        await InsertClientAsync();
        await InsertClientAsync();

        var page = await Repository.ListAsync(1, 1, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.True(page.TotalCount >= 2);
        Assert.Equal(1, page.PageSize);
    }

    [MySqlFact]
    public async Task Secrets_CanHoldSeveralAtOnce_AndRevokeAndUseAreRecorded()
    {
        var client = await InsertClientAsync();
        var first = await InsertSecretAsync(client.Id);
        var second = await InsertSecretAsync(client.Id);

        Assert.True(await Repository.RevokeSecretAsync(first, Instant, CancellationToken.None));
        Assert.False(await Repository.RevokeSecretAsync(first, Instant, CancellationToken.None));
        await Repository.MarkSecretUsedAsync(second, Instant.AddMinutes(1), CancellationToken.None);

        var secrets = await Repository.GetSecretsAsync(client.Id, CancellationToken.None);
        Assert.Equal(2, secrets.Count);
        Assert.Equal(Instant, secrets.Single(s => s.Id == first).RevokedOn);
        Assert.Null(secrets.Single(s => s.Id == second).RevokedOn);
        Assert.Equal(Instant.AddMinutes(1), secrets.Single(s => s.Id == second).LastUsedOn);
    }

    [MySqlFact]
    public async Task ReplaceScopes_SetsExactlyTheGivenScopes()
    {
        var client = await InsertClientAsync();
        var scopes = Get<IScopeRepository>();
        var read = await scopes.InsertAsync(new Scope { Name = Unique("a:read"), CreatedOn = Instant }, CancellationToken.None);
        var write = await scopes.InsertAsync(new Scope { Name = Unique("b:write"), CreatedOn = Instant }, CancellationToken.None);

        await Repository.ReplaceScopesAsync(client.Id, [read, write], "admin", Instant, CancellationToken.None);
        await Repository.ReplaceScopesAsync(client.Id, [write, write], "admin", Instant, CancellationToken.None);

        var granted = await Repository.GetClientScopesAsync(client.Id, CancellationToken.None);
        var names = await Repository.GetScopeNamesAsync(client.Id, CancellationToken.None);
        Assert.Equal(write, Assert.Single(granted).ScopeId);
        Assert.StartsWith("b:write", Assert.Single(names), StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task InsertSecret_ForUnknownClient_IsRejectedByTheForeignKey()
    {
        var secret = new ClientSecret { ClientRefId = long.MaxValue, Secret = "hash", CreatedOn = Instant, CreatedBy = "tests" };

        var exception = await Assert.ThrowsAsync<MySqlException>(() => Repository.InsertSecretAsync(secret, CancellationToken.None));

        Assert.Equal(MySqlErrorCode.NoReferencedRow2, exception.ErrorCode);
    }
}
