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

public interface IAccessTokenRepository
{
    Task<long> InsertAsync(AccessToken token, CancellationToken cancellationToken);

    Task<AccessToken?> GetByJtiAsync(string jti, CancellationToken cancellationToken);

    Task<AccessToken?> GetByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Lists issued tokens, newest first, optionally for one client.</summary>
    Task<PagedResult<AccessToken>> ListAsync(long? clientRefId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Revokes a token that is not already revoked. Returns false when nothing changed.</summary>
    Task<bool> RevokeAsync(long id, string reason, DateTimeOffset revokedOn, CancellationToken cancellationToken);
}
