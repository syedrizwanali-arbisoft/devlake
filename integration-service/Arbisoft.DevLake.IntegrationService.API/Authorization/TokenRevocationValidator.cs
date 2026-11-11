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

using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Arbisoft.DevLake.IntegrationService.API.Authorization;

/// <summary>
/// Runs after a token's signature, issuer, audience and expiry check out. It rejects the token when the Auth API never issued it
/// (no AccessTokens row for its jti), when it was revoked, or when its client has been deactivated, so a revocation or a deactivation takes
/// effect before the token expires.
/// </summary>
public sealed class TokenRevocationValidator
{
    private readonly IAccessTokenRepository _accessTokens;
    private readonly IClientRepository _clients;
    private readonly ILogger<TokenRevocationValidator> _logger;

    public TokenRevocationValidator(IAccessTokenRepository accessTokens, IClientRepository clients, ILogger<TokenRevocationValidator> logger)
    {
        _accessTokens = accessTokens;
        _clients = clients;
        _logger = logger;
    }

    public async Task ValidateAsync(TokenValidatedContext context)
    {
        var jti = (context.SecurityToken as JsonWebToken)?.Id;
        if (string.IsNullOrEmpty(jti))
        {
            context.Fail("The token has no jti.");
            return;
        }

        var stored = await _accessTokens.GetByJtiAsync(jti, context.HttpContext.RequestAborted);
        if (stored is null)
        {
            _logger.LogWarning("Rejected a validly signed token that was never issued, jti {Jti}", jti);
            context.Fail("The token was not issued by this service.");
        }
        else if (stored.RevokedOn is not null)
        {
            _logger.LogInformation("Rejected revoked token {Jti}", jti);
            context.Fail("The token has been revoked.");
        }
        else if (await _clients.GetByIdAsync(stored.ClientRefId, context.HttpContext.RequestAborted) is not { IsActive: true })
        {
            _logger.LogInformation("Rejected token {Jti} of a client that is no longer active", jti);
            context.Fail("The client is not active.");
        }
    }
}
