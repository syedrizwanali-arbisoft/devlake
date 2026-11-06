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

public sealed class EmailMappingRepository(IDbConnectionFactory connectionFactory) : IEmailMappingRepository
{
    public async Task<IReadOnlyList<EmailMapping>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<EmailMapping>(new CommandDefinition(
            "SELECT * FROM EmailMappings ORDER BY arbisoftEmail, email", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<EmailMapping?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<EmailMapping>(new CommandDefinition(
            "SELECT * FROM EmailMappings WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetMappedEmailsAsync(string arbisoftEmail, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT email FROM EmailMappings WHERE arbisoftEmail = @arbisoftEmail",
            new { arbisoftEmail }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<long> AddAsync(EmailMapping mapping, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO EmailMappings (arbisoftEmail, email, createdOn)
            VALUES (@ArbisoftEmail, @Email, @CreatedOn);
            SELECT LAST_INSERT_ID();
            """, mapping, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(EmailMapping mapping, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE EmailMappings SET arbisoftEmail = @ArbisoftEmail, email = @Email WHERE id = @Id",
            mapping, cancellationToken: cancellationToken)) > 0;
    }
}
