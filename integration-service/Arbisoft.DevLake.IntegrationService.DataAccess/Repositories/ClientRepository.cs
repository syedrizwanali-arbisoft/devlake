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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public sealed class ClientRepository : RepositoryBase, IClientRepository
{
    private const string Columns = "id, clientId, name, isActive, accessTokenLifetimeSeconds, createdOn, createdBy, modifiedOn, modifiedBy";
    private const string SecretColumns = "id, clientRefId, secret, expiresOn, revokedOn, lastUsedOn, createdOn, createdBy";

    public ClientRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<Client?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Client>($"SELECT {Columns} FROM Clients WHERE clientId = @clientId", new { clientId }, cancellationToken);
    }

    public Task<Client?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Client>($"SELECT {Columns} FROM Clients WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<PagedResult<Client>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return QueryPageAsync<Client>(Columns, "FROM Clients", "name, id", null, page, pageSize, cancellationToken);
    }

    public Task<long> InsertAsync(Client client, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO Clients (clientId, name, isActive, accessTokenLifetimeSeconds, createdOn, createdBy, modifiedOn, modifiedBy) " +
            "VALUES (@ClientId, @Name, @IsActive, @AccessTokenLifetimeSeconds, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy)",
            client,
            cancellationToken);
    }

    public async Task<bool> UpdateAsync(Client client, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync(
            "UPDATE Clients SET name = @Name, isActive = @IsActive, accessTokenLifetimeSeconds = @AccessTokenLifetimeSeconds, " +
            "modifiedOn = @ModifiedOn, modifiedBy = @ModifiedBy WHERE id = @Id",
            client,
            cancellationToken);
        return changed > 0;
    }

    public Task<IReadOnlyList<ClientSecret>> GetSecretsAsync(long clientRefId, CancellationToken cancellationToken)
    {
        return QueryAsync<ClientSecret>($"SELECT {SecretColumns} FROM ClientSecrets WHERE clientRefId = @clientRefId ORDER BY createdOn DESC, id DESC", new { clientRefId }, cancellationToken);
    }

    public Task<long> InsertSecretAsync(ClientSecret secret, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO ClientSecrets (clientRefId, secret, expiresOn, revokedOn, lastUsedOn, createdOn, createdBy) " +
            "VALUES (@ClientRefId, @Secret, @ExpiresOn, @RevokedOn, @LastUsedOn, @CreatedOn, @CreatedBy)",
            secret,
            cancellationToken);
    }

    public async Task<bool> RevokeSecretAsync(long secretId, DateTimeOffset revokedOn, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync("UPDATE ClientSecrets SET revokedOn = @revokedOn WHERE id = @secretId AND revokedOn IS NULL", new { secretId, revokedOn }, cancellationToken);
        return changed > 0;
    }

    public async Task MarkSecretUsedAsync(long secretId, DateTimeOffset usedOn, CancellationToken cancellationToken)
    {
        await ExecuteAsync("UPDATE ClientSecrets SET lastUsedOn = @usedOn WHERE id = @secretId", new { secretId, usedOn }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetScopeNamesAsync(long clientRefId, CancellationToken cancellationToken)
    {
        return QueryAsync<string>(
            "SELECT s.name FROM ClientScopes cs INNER JOIN Scopes s ON s.id = cs.scopeId WHERE cs.clientRefId = @clientRefId ORDER BY s.name",
            new { clientRefId },
            cancellationToken);
    }

    public Task<IReadOnlyList<ClientScope>> GetClientScopesAsync(long clientRefId, CancellationToken cancellationToken)
    {
        return QueryAsync<ClientScope>("SELECT id, clientRefId, scopeId, createdOn, createdBy FROM ClientScopes WHERE clientRefId = @clientRefId ORDER BY scopeId", new { clientRefId }, cancellationToken);
    }

    public async Task ReplaceScopesAsync(
        long clientRefId, IReadOnlyCollection<long> scopeIds, string createdBy, DateTimeOffset createdOn, CancellationToken cancellationToken)
    {
        await ExecuteAsync("DELETE FROM ClientScopes WHERE clientRefId = @clientRefId", new { clientRefId }, cancellationToken);
        foreach (var scopeId in scopeIds.Distinct())
        {
            await ExecuteAsync(
                "INSERT INTO ClientScopes (clientRefId, scopeId, createdOn, createdBy) VALUES (@clientRefId, @scopeId, @createdOn, @createdBy)",
                new { clientRefId, scopeId, createdOn, createdBy },
                cancellationToken);
        }
    }
}
