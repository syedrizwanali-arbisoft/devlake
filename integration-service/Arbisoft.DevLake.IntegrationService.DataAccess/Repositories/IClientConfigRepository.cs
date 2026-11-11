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

/// <summary>The ClientConfig aggregate: how the service sends data to a client, and the URLs it sends to.</summary>
public interface IClientConfigRepository
{
    Task<ClientConfig?> GetByClientRefIdAsync(long clientRefId, CancellationToken cancellationToken);

    Task<ClientConfig?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<PagedResult<ClientConfig>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<long> InsertAsync(ClientConfig config, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(ClientConfig config, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientConfigOutboundUrl>> GetOutboundUrlsAsync(long clientConfigId, CancellationToken cancellationToken);

    Task<ClientConfigOutboundUrl?> GetOutboundUrlByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>The active outbound URL for an event type, or null when the client has no active one.</summary>
    Task<ClientConfigOutboundUrl?> GetActiveOutboundUrlAsync(long clientConfigId, string eventType, CancellationToken cancellationToken);

    Task<long> InsertOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken);

    Task<bool> UpdateOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken);
}
