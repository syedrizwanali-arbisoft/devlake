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

using Dapper;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public sealed class ClientRepository(IDbConnectionFactory connectionFactory) : IClientRepository
{
    public async Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Client>(new CommandDefinition(
            "SELECT * FROM Clients ORDER BY name", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Client?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Client>(new CommandDefinition(
            "SELECT * FROM Clients WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<Client?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Client>(new CommandDefinition(
            "SELECT * FROM Clients WHERE clientId = @clientId", new { clientId }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO Clients (clientId, name, isActive, accessTokenLifetimeSeconds, createdOn, createdBy)
            VALUES (@ClientId, @Name, @IsActive, @AccessTokenLifetimeSeconds, @CreatedOn, @CreatedBy);
            SELECT LAST_INSERT_ID();
            """, client, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Client client, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Clients
            SET name = @Name, isActive = @IsActive, accessTokenLifetimeSeconds = @AccessTokenLifetimeSeconds,
                modifiedOn = @ModifiedOn, modifiedBy = @ModifiedBy
            WHERE id = @Id
            """, client, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<ClientSecret>> GetSecretsAsync(long clientRefId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClientSecret>(new CommandDefinition(
            "SELECT * FROM ClientSecrets WHERE clientRefId = @clientRefId ORDER BY createdOn DESC",
            new { clientRefId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClientSecret>> GetUsableSecretsAsync(long clientRefId, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClientSecret>(new CommandDefinition(
            """
            SELECT * FROM ClientSecrets
            WHERE clientRefId = @clientRefId AND revokedOn IS NULL AND expiresOn > @now
            ORDER BY createdOn DESC
            """, new { clientRefId, now }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<long> AddSecretAsync(ClientSecret secret, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO ClientSecrets (clientRefId, secret, expiresOn, createdOn, createdBy)
            VALUES (@ClientRefId, @Secret, @ExpiresOn, @CreatedOn, @CreatedBy);
            SELECT LAST_INSERT_ID();
            """, secret, cancellationToken: cancellationToken));
    }

    public async Task<bool> RevokeSecretAsync(long secretId, DateTime revokedOn, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ClientSecrets SET revokedOn = @revokedOn WHERE id = @secretId AND revokedOn IS NULL",
            new { secretId, revokedOn }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task UpdateSecretLastUsedAsync(long secretId, DateTime lastUsedOn, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ClientSecrets SET lastUsedOn = @lastUsedOn WHERE id = @secretId",
            new { secretId, lastUsedOn }, cancellationToken: cancellationToken));
    }
}
