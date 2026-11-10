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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IAccessTokenService
{
    Task<IReadOnlyList<AccessTokenDto>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task<AccessTokenDto?> GetAsync(string jti, CancellationToken cancellationToken = default);

    /// <summary>Revokes a token before it expires. The API rejects it on its next use.</summary>
    Task<AdminResult> RevokeAsync(string jti, string reason, string revokedBy, CancellationToken cancellationToken = default);
}

public sealed class AccessTokenService(
    IAccessTokenRepository accessTokens,
    IClientRepository clients,
    TimeProvider timeProvider) : IAccessTokenService
{
    public async Task<IReadOnlyList<AccessTokenDto>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var tokens = await accessTokens.GetRecentAsync(Math.Clamp(limit, 1, 1000), cancellationToken);
        var clientsById = (await clients.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id);
        var now = Now();
        return tokens.Select(t =>
        {
            clientsById.TryGetValue(t.ClientRefId, out var client);
            return ToDto(t, client?.ClientId ?? "(unknown)", client?.Name ?? "(unknown)", now);
        }).ToList();
    }

    public async Task<AccessTokenDto?> GetAsync(string jti, CancellationToken cancellationToken = default)
    {
        var token = await accessTokens.GetByJtiAsync(jti, cancellationToken);
        if (token is null)
        {
            return null;
        }

        var client = await clients.GetByIdAsync(token.ClientRefId, cancellationToken);
        return ToDto(token, client?.ClientId ?? "(unknown)", client?.Name ?? "(unknown)", Now());
    }

    public async Task<AdminResult> RevokeAsync(string jti, string reason, string revokedBy, CancellationToken cancellationToken = default)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 900)
        {
            return AdminResult.Fail("A reason of up to 900 characters is required.");
        }

        var token = await accessTokens.GetByJtiAsync(jti, cancellationToken);
        if (token is null)
        {
            return AdminResult.Fail("The token was not found.");
        }

        if (token.RevokedOn is not null)
        {
            return AdminResult.Fail("The token is already revoked.");
        }

        // AccessTokens has no revokedBy column, so the admin is recorded with the reason.
        await accessTokens.RevokeAsync(jti, $"{trimmed} (revoked by {revokedBy})", Now(), cancellationToken);
        return AdminResult.Ok;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static AccessTokenDto ToDto(DataAccess.Models.AccessToken t, string clientId, string clientName, DateTime now) => new(
        t.Jti, clientId, clientName, t.GrantedScopes, t.IssuedOn, t.ExpiresOn, t.RevokedOn, t.RevokedReason, t.IssuedToIp,
        t.RevokedOn is not null ? "Revoked" : t.ExpiresOn <= now ? "Expired" : "Active");
}
