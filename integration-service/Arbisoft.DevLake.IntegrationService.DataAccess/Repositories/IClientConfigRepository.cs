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

public interface IClientConfigRepository
{
    Task<ClientConfig?> GetByClientRefIdAsync(long clientRefId, CancellationToken cancellationToken = default);
    Task<long> AddAsync(ClientConfig config, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(ClientConfig config, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientConfigOutboundUrl>> GetOutboundUrlsAsync(long clientConfigId, CancellationToken cancellationToken = default);

    /// <summary>Returns the client's callback for the event type, only if both the config and the URL are active.</summary>
    Task<ClientConfigOutboundUrl?> GetActiveOutboundUrlAsync(long clientRefId, string eventType, CancellationToken cancellationToken = default);
    Task<long> AddOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken = default);
    Task<bool> UpdateOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken = default);
}
