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

public sealed class SigningKeyRepository : RepositoryBase, ISigningKeyRepository
{
    private const string Columns = "id, kid, algorithm, publicKey, privateKeyRef, activatedOn, retiredOn, createdOn";

    public SigningKeyRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<SigningKey?> GetActiveAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SigningKey>(
            $"SELECT {Columns} FROM SigningKeys WHERE activatedOn <= @at AND retiredOn IS NULL ORDER BY activatedOn DESC, id DESC LIMIT 1",
            new { at },
            cancellationToken);
    }

    public Task<SigningKey?> GetByKidAsync(string kid, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SigningKey>($"SELECT {Columns} FROM SigningKeys WHERE kid = @kid", new { kid }, cancellationToken);
    }

    public Task<PagedResult<SigningKey>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return QueryPageAsync<SigningKey>(Columns, "FROM SigningKeys", "activatedOn DESC, id DESC", null, page, pageSize, cancellationToken);
    }

    public Task<long> InsertAsync(SigningKey key, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO SigningKeys (kid, algorithm, publicKey, privateKeyRef, activatedOn, retiredOn, createdOn) " +
            "VALUES (@Kid, @Algorithm, @PublicKey, @PrivateKeyRef, @ActivatedOn, @RetiredOn, @CreatedOn)",
            key,
            cancellationToken);
    }

    public async Task<bool> RetireAsync(long id, DateTimeOffset retiredOn, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync("UPDATE SigningKeys SET retiredOn = @retiredOn WHERE id = @id AND retiredOn IS NULL", new { id, retiredOn }, cancellationToken);
        return changed > 0;
    }
}
