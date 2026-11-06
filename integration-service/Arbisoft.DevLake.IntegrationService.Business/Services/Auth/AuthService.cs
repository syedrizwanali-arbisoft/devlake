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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Auth;

public sealed class AuthService(
    IClientRepository clients,
    IScopeRepository scopes,
    IAccessTokenRepository accessTokens,
    ISigningKeyProvider signingKeys,
    ITokenGenerator tokenGenerator,
    ISecretHasher secretHasher,
    TimeProvider timeProvider) : IAuthService
{
    private const string ClientCredentialsGrant = "client_credentials";
    private const string BearerTokenType = "Bearer";

    public async Task<TokenResult> IssueTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        if (request.GrantType != ClientCredentialsGrant)
        {
            return TokenResult.Failure(TokenError.UnsupportedGrantType);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var client = await clients.GetByClientIdAsync(request.ClientId, cancellationToken);
        if (client is not { IsActive: true })
        {
            return TokenResult.Failure(TokenError.InvalidClient);
        }

        var usableSecrets = await clients.GetUsableSecretsAsync(client.Id, now, cancellationToken);
        var secret = usableSecrets.FirstOrDefault(s => secretHasher.Verify(request.ClientSecret, s.Secret));
        if (secret is null)
        {
            return TokenResult.Failure(TokenError.InvalidClient);
        }

        var grantedScopes = await scopes.GetNamesForClientAsync(client.Id, cancellationToken);
        var signingKey = await signingKeys.GetActiveAsync(cancellationToken);
        var expiresOn = now.AddSeconds(client.AccessTokenLifetimeSeconds);
        var jti = Guid.NewGuid().ToString();
        var scopeText = string.Join(' ', grantedScopes);

        var token = tokenGenerator.Generate(
            new TokenClaims(jti, client.ClientId, grantedScopes, now, expiresOn),
            signingKey.Credentials);

        // The token must be saved before it is returned, because the API rejects any jti it can't find.
        await accessTokens.AddAsync(new AccessToken
        {
            Jti = jti,
            ClientRefId = client.Id,
            ClientSecretId = secret.Id,
            SigningKeyId = signingKey.SigningKeyId,
            GrantedScopes = scopeText,
            IssuedOn = now,
            ExpiresOn = expiresOn,
            IssuedToIp = request.CallerIp,
        }, cancellationToken);
        await clients.UpdateSecretLastUsedAsync(secret.Id, now, cancellationToken);

        return TokenResult.Success(new TokenResponse(token, BearerTokenType, client.AccessTokenLifetimeSeconds, scopeText));
    }

    public async Task<bool> IsTokenActiveAsync(string jti, CancellationToken cancellationToken = default)
    {
        var token = await accessTokens.GetByJtiAsync(jti, cancellationToken);
        return token is { RevokedOn: null };
    }
}
