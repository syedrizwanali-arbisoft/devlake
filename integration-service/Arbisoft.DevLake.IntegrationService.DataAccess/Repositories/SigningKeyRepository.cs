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

public sealed class SigningKeyRepository(IDbConnectionFactory connectionFactory) : ISigningKeyRepository
{
    public async Task<IReadOnlyList<SigningKey>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SigningKey>(new CommandDefinition(
            "SELECT * FROM SigningKeys ORDER BY activatedOn DESC", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SigningKey?> GetActiveAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<SigningKey>(new CommandDefinition(
            """
            SELECT * FROM SigningKeys
            WHERE activatedOn <= @now AND (retiredOn IS NULL OR retiredOn > @now)
            ORDER BY activatedOn DESC
            LIMIT 1
            """, new { now }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddAsync(SigningKey key, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO SigningKeys (kid, algorithm, publicKey, privateKeyRef, activatedOn, createdOn)
            VALUES (@Kid, @Algorithm, @PublicKey, @PrivateKeyRef, @ActivatedOn, @CreatedOn);
            SELECT LAST_INSERT_ID();
            """, key, cancellationToken: cancellationToken));
    }

    public async Task<bool> RetireAsync(long id, DateTime retiredOn, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE SigningKeys SET retiredOn = @retiredOn WHERE id = @id AND retiredOn IS NULL",
            new { id, retiredOn }, cancellationToken: cancellationToken)) > 0;
    }
}
