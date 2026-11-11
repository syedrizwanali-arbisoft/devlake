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
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IAccessTokenService
{
    /// <summary>The most recently issued tokens, newest first, optionally for one client.</summary>
    Task<IReadOnlyList<AccessTokenDto>> ListRecentAsync(long? clientRefId, int limit, CancellationToken cancellationToken);

    Task<AccessTokenDto?> GetAsync(string jti, CancellationToken cancellationToken);

    /// <summary>Revokes a token. The API refuses it from then on, even before it expires.</summary>
    Task<AdminResult> RevokeAsync(string jti, string reason, string revokedBy, CancellationToken cancellationToken);
}

public sealed class AccessTokenService : IAccessTokenService
{
    public const int MaxReasonLength = 300;
    private const int ColumnLength = 500;

    private readonly IAccessTokenRepository _tokens;
    private readonly IClientRepository _clients;
    private readonly TimeProvider _timeProvider;

    public AccessTokenService(IAccessTokenRepository tokens, IClientRepository clients, TimeProvider timeProvider)
    {
        _tokens = tokens;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<AccessTokenDto>> ListRecentAsync(long? clientRefId, int limit, CancellationToken cancellationToken)
    {
        var page = await _tokens.ListAsync(clientRefId, 1, Math.Clamp(limit, 1, AdminPaging.MaxRows), cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var names = new Dictionary<long, Client?>();
        var result = new List<AccessTokenDto>(page.Items.Count);
        foreach (var token in page.Items)
        {
            if (!names.TryGetValue(token.ClientRefId, out var client))
            {
                client = await _clients.GetByIdAsync(token.ClientRefId, cancellationToken);
                names[token.ClientRefId] = client;
            }

            result.Add(ToDto(token, client, now));
        }

        return result;
    }

    public async Task<AccessTokenDto?> GetAsync(string jti, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetByJtiAsync(jti, cancellationToken);
        return token is null ? null : ToDto(token, await _clients.GetByIdAsync(token.ClientRefId, cancellationToken), _timeProvider.GetUtcNow());
    }

    public async Task<AdminResult> RevokeAsync(string jti, string reason, string revokedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return AdminResult.Fail($"Give a reason of up to {MaxReasonLength} characters.");
        }

        var token = await _tokens.GetByJtiAsync(jti, cancellationToken);
        if (token is null)
        {
            return AdminResult.Fail("The token does not exist.");
        }

        if (token.RevokedOn is not null)
        {
            return AdminResult.Fail("The token is already revoked.");
        }

        // The table has no "revoked by" column, so the reason records who did it.
        var recorded = $"{reason.Trim()} (revoked by {revokedBy})";
        var changed = await _tokens.RevokeAsync(token.Id, recorded.Length > ColumnLength ? recorded[..ColumnLength] : recorded, _timeProvider.GetUtcNow(), cancellationToken);
        return changed ? AdminResult.Ok : AdminResult.Fail("The token is already revoked.");
    }

    private static AccessTokenDto ToDto(AccessToken token, Client? client, DateTimeOffset now)
    {
        var status = token.RevokedOn is not null ? "Revoked" : token.ExpiresOn <= now ? "Expired" : "Active";
        return new AccessTokenDto(
            token.Jti, client?.ClientId ?? string.Empty, client?.Name ?? string.Empty, token.GrantedScopes, token.IssuedOn, token.ExpiresOn,
            token.RevokedOn, token.RevokedReason, token.IssuedToIp, status);
    }
}
