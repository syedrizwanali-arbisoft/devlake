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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public interface IEmailMappingRepository
{
    /// <summary>The other emails an engineer commits with, not including the Arbisoft email itself.</summary>
    Task<IReadOnlyList<string>> GetMappedEmailsAsync(string arbisoftEmail, CancellationToken cancellationToken);

    /// <summary>Lists mappings, optionally only those where either address contains <paramref name="search"/> (already normalized by the caller).</summary>
    Task<PagedResult<EmailMapping>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>True when the pair exists, ignoring the mapping with id <paramref name="exceptId"/> so an edit does not clash with itself.</summary>
    Task<bool> ExistsAsync(string arbisoftEmail, string email, long? exceptId, CancellationToken cancellationToken);

    Task<EmailMapping?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<long> InsertAsync(EmailMapping mapping, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(EmailMapping mapping, CancellationToken cancellationToken);
}
