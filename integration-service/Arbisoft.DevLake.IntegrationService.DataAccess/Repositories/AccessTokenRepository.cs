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

using Dapper;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public sealed class AccessTokenRepository(IDbConnectionFactory connectionFactory) : IAccessTokenRepository
{
    public async Task<long> AddAsync(AccessToken token, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO AccessTokens
                (jti, clientRefId, clientSecretId, signingKeyId, grantedScopes, issuedOn, expiresOn, issuedToIp)
            VALUES
                (@Jti, @ClientRefId, @ClientSecretId, @SigningKeyId, @GrantedScopes, @IssuedOn, @ExpiresOn, @IssuedToIp);
            SELECT LAST_INSERT_ID();
            """, token, cancellationToken: cancellationToken));
    }

    public async Task<AccessToken?> GetByJtiAsync(string jti, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccessToken>(new CommandDefinition(
            "SELECT * FROM AccessTokens WHERE jti = @jti", new { jti }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AccessToken>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AccessToken>(new CommandDefinition(
            "SELECT * FROM AccessTokens ORDER BY issuedOn DESC LIMIT @limit", new { limit }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> RevokeAsync(string jti, string reason, DateTime revokedOn, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE AccessTokens SET revokedOn = @revokedOn, revokedReason = @reason WHERE jti = @jti AND revokedOn IS NULL",
            new { jti, reason, revokedOn }, cancellationToken: cancellationToken)) > 0;
    }
}
