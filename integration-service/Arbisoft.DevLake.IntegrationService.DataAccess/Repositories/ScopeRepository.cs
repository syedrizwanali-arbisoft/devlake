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

public sealed class ScopeRepository(IDbConnectionFactory connectionFactory) : IScopeRepository
{
    public async Task<IReadOnlyList<Scope>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Scope>(new CommandDefinition(
            "SELECT * FROM Scopes ORDER BY name", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Scope?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Scope>(new CommandDefinition(
            "SELECT * FROM Scopes WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddAsync(Scope scope, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO Scopes (name, description, createdOn) VALUES (@Name, @Description, @CreatedOn);
            SELECT LAST_INSERT_ID();
            """, scope, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Scope scope, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Scopes SET name = @Name, description = @Description WHERE id = @Id",
            scope, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<string>> GetNamesForClientAsync(long clientRefId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT s.name FROM ClientScopes cs
            JOIN Scopes s ON s.id = cs.scopeId
            WHERE cs.clientRefId = @clientRefId
            ORDER BY s.name
            """, new { clientRefId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task AssignToClientAsync(ClientScope clientScope, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT IGNORE INTO ClientScopes (clientRefId, scopeId, createdOn, createdBy)
            VALUES (@ClientRefId, @ScopeId, @CreatedOn, @CreatedBy)
            """, clientScope, cancellationToken: cancellationToken));
    }

    public async Task RemoveFromClientAsync(long clientRefId, long scopeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ClientScopes WHERE clientRefId = @clientRefId AND scopeId = @scopeId",
            new { clientRefId, scopeId }, cancellationToken: cancellationToken));
    }
}
