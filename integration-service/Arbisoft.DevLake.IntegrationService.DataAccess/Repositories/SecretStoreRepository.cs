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

using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public sealed class SecretStoreRepository : RepositoryBase, ISecretStoreRepository
{
    public SecretStoreRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<string?> GetEncryptedValueAsync(string secretRef, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string>("SELECT encryptedValue FROM SecretStoreEntries WHERE secretRef = @secretRef", new { secretRef }, cancellationToken);
    }

    public async Task SaveEncryptedValueAsync(string secretRef, string encryptedValue, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            "INSERT INTO SecretStoreEntries (secretRef, encryptedValue, createdOn) VALUES (@secretRef, @encryptedValue, @at) AS incoming " +
            "ON DUPLICATE KEY UPDATE encryptedValue = incoming.encryptedValue, modifiedOn = @at",
            new { secretRef, encryptedValue, at },
            cancellationToken);
    }
}
