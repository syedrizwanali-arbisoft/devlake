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

public sealed class ScopeRepository : RepositoryBase, IScopeRepository
{
    private const string Columns = "id, name, description, createdOn";

    public ScopeRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<PagedResult<Scope>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return QueryPageAsync<Scope>(Columns, "FROM Scopes", "name", null, page, pageSize, cancellationToken);
    }

    public Task<Scope?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Scope>($"SELECT {Columns} FROM Scopes WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<Scope?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Scope>($"SELECT {Columns} FROM Scopes WHERE name = @name", new { name }, cancellationToken);
    }

    public Task<long> InsertAsync(Scope scope, CancellationToken cancellationToken)
    {
        return InsertAsync("INSERT INTO Scopes (name, description, createdOn) VALUES (@Name, @Description, @CreatedOn)", scope, cancellationToken);
    }

    public async Task<bool> UpdateAsync(Scope scope, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync("UPDATE Scopes SET name = @Name, description = @Description WHERE id = @Id", scope, cancellationToken);
        return changed > 0;
    }
}
