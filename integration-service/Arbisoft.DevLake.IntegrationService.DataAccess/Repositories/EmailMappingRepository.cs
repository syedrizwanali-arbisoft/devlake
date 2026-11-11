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

public sealed class EmailMappingRepository : RepositoryBase, IEmailMappingRepository
{
    private const string Columns = "id, arbisoftEmail, email, createdOn";

    public EmailMappingRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<IReadOnlyList<string>> GetMappedEmailsAsync(string arbisoftEmail, CancellationToken cancellationToken)
    {
        return QueryAsync<string>("SELECT email FROM EmailMappings WHERE arbisoftEmail = @arbisoftEmail ORDER BY email", new { arbisoftEmail }, cancellationToken);
    }

    public Task<PagedResult<EmailMapping>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return QueryPageAsync<EmailMapping>(Columns, "FROM EmailMappings", "arbisoftEmail, email", null, page, pageSize, cancellationToken);
    }

    public Task<EmailMapping?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<EmailMapping>($"SELECT {Columns} FROM EmailMappings WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<long> InsertAsync(EmailMapping mapping, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO EmailMappings (arbisoftEmail, email, createdOn) VALUES (@ArbisoftEmail, @Email, @CreatedOn)",
            mapping,
            cancellationToken);
    }

    public async Task<bool> UpdateAsync(EmailMapping mapping, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync("UPDATE EmailMappings SET arbisoftEmail = @ArbisoftEmail, email = @Email WHERE id = @Id", mapping, cancellationToken);
        return changed > 0;
    }
}
