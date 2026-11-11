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

/// <summary>The Client aggregate: the client, its secrets and the scopes it may request.</summary>
public interface IClientRepository
{
    Task<Client?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken);

    Task<Client?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<PagedResult<Client>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<long> InsertAsync(Client client, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Client client, CancellationToken cancellationToken);

    /// <summary>All of a client's secrets, including revoked and expired ones, newest first.</summary>
    Task<IReadOnlyList<ClientSecret>> GetSecretsAsync(long clientRefId, CancellationToken cancellationToken);

    Task<long> InsertSecretAsync(ClientSecret secret, CancellationToken cancellationToken);

    /// <summary>Revokes a secret that is not already revoked. Returns false when nothing changed.</summary>
    Task<bool> RevokeSecretAsync(long secretId, DateTimeOffset revokedOn, CancellationToken cancellationToken);

    Task MarkSecretUsedAsync(long secretId, DateTimeOffset usedOn, CancellationToken cancellationToken);

    /// <summary>The names of the scopes granted to a client.</summary>
    Task<IReadOnlyList<string>> GetScopeNamesAsync(long clientRefId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientScope>> GetClientScopesAsync(long clientRefId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the scopes granted to a client. The delete and the inserts are separate statements, so the caller
    /// must run this inside a transaction.
    /// </summary>
    Task ReplaceScopesAsync(long clientRefId, IReadOnlyCollection<long> scopeIds, string createdBy, DateTimeOffset createdOn, CancellationToken cancellationToken);
}
