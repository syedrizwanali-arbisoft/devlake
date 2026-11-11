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

public interface ISigningKeyRepository
{
    /// <summary>The newest key that is active at <paramref name="at"/>: activated, and not retired.</summary>
    Task<SigningKey?> GetActiveAsync(DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Finds a key by its kid, including retired keys, so older tokens still verify.</summary>
    Task<SigningKey?> GetByKidAsync(string kid, CancellationToken cancellationToken);

    Task<PagedResult<SigningKey>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<long> InsertAsync(SigningKey key, CancellationToken cancellationToken);

    /// <summary>Retires a key that is still active. Returns false when it was already retired or does not exist.</summary>
    Task<bool> RetireAsync(long id, DateTimeOffset retiredOn, CancellationToken cancellationToken);
}
