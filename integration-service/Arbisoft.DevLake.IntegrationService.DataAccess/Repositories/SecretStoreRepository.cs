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

public sealed class SecretStoreRepository(IDbConnectionFactory connectionFactory) : ISecretStoreRepository
{
    public async Task<SecretStoreEntry?> GetAsync(string secretRef, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SecretStoreEntry>(new CommandDefinition(
            "SELECT * FROM SecretStoreEntries WHERE secretRef = @secretRef", new { secretRef }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddAsync(SecretStoreEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO SecretStoreEntries (secretRef, valueEncrypted, createdOn, createdBy)
            VALUES (@SecretRef, @ValueEncrypted, @CreatedOn, @CreatedBy);
            SELECT LAST_INSERT_ID();
            """, entry, cancellationToken: cancellationToken));
    }
}
