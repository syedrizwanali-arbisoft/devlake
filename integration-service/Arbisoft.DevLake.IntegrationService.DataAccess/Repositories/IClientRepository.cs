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

public interface IClientRepository
{
    Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Client?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Client?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
    Task<long> AddAsync(Client client, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Client client, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientSecret>> GetSecretsAsync(long clientRefId, CancellationToken cancellationToken = default);

    /// <summary>Returns the secrets that are neither revoked nor expired at <paramref name="now"/>.</summary>
    Task<IReadOnlyList<ClientSecret>> GetUsableSecretsAsync(long clientRefId, DateTime now, CancellationToken cancellationToken = default);
    Task<long> AddSecretAsync(ClientSecret secret, CancellationToken cancellationToken = default);
    Task<bool> RevokeSecretAsync(long secretId, DateTime revokedOn, CancellationToken cancellationToken = default);
    Task UpdateSecretLastUsedAsync(long secretId, DateTime lastUsedOn, CancellationToken cancellationToken = default);
}
