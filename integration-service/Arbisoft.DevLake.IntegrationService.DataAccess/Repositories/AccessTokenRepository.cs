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

public sealed class AccessTokenRepository : RepositoryBase, IAccessTokenRepository
{
    private const string Columns =
        "id, jti, clientRefId, clientSecretId, signingKeyId, grantedScopes, issuedOn, expiresOn, revokedOn, revokedReason, issuedToIp";

    public AccessTokenRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<long> InsertAsync(AccessToken token, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO AccessTokens (jti, clientRefId, clientSecretId, signingKeyId, grantedScopes, issuedOn, expiresOn, revokedOn, revokedReason, issuedToIp) " +
            "VALUES (@Jti, @ClientRefId, @ClientSecretId, @SigningKeyId, @GrantedScopes, @IssuedOn, @ExpiresOn, @RevokedOn, @RevokedReason, @IssuedToIp)",
            token,
            cancellationToken);
    }

    public Task<AccessToken?> GetByJtiAsync(string jti, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<AccessToken>($"SELECT {Columns} FROM AccessTokens WHERE jti = @jti", new { jti }, cancellationToken);
    }

    public Task<AccessToken?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<AccessToken>($"SELECT {Columns} FROM AccessTokens WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<PagedResult<AccessToken>> ListAsync(long? clientRefId, int page, int pageSize, CancellationToken cancellationToken)
    {
        return QueryPageAsync<AccessToken>(
            Columns, "FROM AccessTokens WHERE (@clientRefId IS NULL OR clientRefId = @clientRefId)", "issuedOn DESC, id DESC", new { clientRefId }, page, pageSize, cancellationToken);
    }

    public async Task<bool> RevokeAsync(long id, string reason, DateTimeOffset revokedOn, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync(
            "UPDATE AccessTokens SET revokedOn = @revokedOn, revokedReason = @reason WHERE id = @id AND revokedOn IS NULL",
            new { id, reason, revokedOn },
            cancellationToken);
        return changed > 0;
    }
}
