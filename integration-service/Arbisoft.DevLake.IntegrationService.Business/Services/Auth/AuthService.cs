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

using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Auth;

public sealed partial class AuthService : IAuthService
{
    private const int MaxLoggedClientIdLength = 100;
    private const string DummySecret = "unused-secret-for-timing";

    private readonly IClientRepository _clients;
    private readonly IAccessTokenRepository _accessTokens;
    private readonly IClientSecretHasher _hasher;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IDbSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;
    private readonly Lazy<string> _dummyHash;

    public AuthService(
        IClientRepository clients,
        IAccessTokenRepository accessTokens,
        IClientSecretHasher hasher,
        ITokenGenerator tokenGenerator,
        IDbSession session,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _clients = clients;
        _accessTokens = accessTokens;
        _hasher = hasher;
        _tokenGenerator = tokenGenerator;
        _session = session;
        _timeProvider = timeProvider;
        _logger = logger;
        _dummyHash = new Lazy<string>(() => hasher.Hash(DummySecret));
    }

    public async Task<AuthResult> AuthenticateAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByClientIdAsync(request.ClientId, cancellationToken);
        if (client is null)
        {
            _hasher.Verify(request.ClientSecret, _dummyHash.Value);
            return Refuse(request.ClientId, AuthFailureReason.UnknownClient);
        }

        var secret = await FindMatchingSecretAsync(client, request.ClientSecret, cancellationToken);
        if (secret is null)
        {
            return Refuse(request.ClientId, AuthFailureReason.InvalidSecret);
        }

        if (!client.IsActive)
        {
            return Refuse(request.ClientId, AuthFailureReason.InactiveClient);
        }

        var scopes = await _clients.GetScopeNamesAsync(client.Id, cancellationToken);
        var issued = await _tokenGenerator.GenerateAsync(new TokenSpecification(client.ClientId, scopes, client.AccessTokenLifetimeSeconds), cancellationToken);
        await RecordAsync(client, secret, issued, scopes, request.CallerIp, cancellationToken);

        return AuthResult.Success(new TokenResponse(issued.AccessToken, AuthConstants.BearerTokenType, client.AccessTokenLifetimeSeconds, string.Join(' ', scopes)));
    }

    /// <summary>A secret counts when it is not revoked, not expired and matches. A client may hold several during rotation.</summary>
    private async Task<ClientSecret?> FindMatchingSecretAsync(Client client, string presentedSecret, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var secrets = await _clients.GetSecretsAsync(client.Id, cancellationToken);
        return secrets
            .Where(secret => secret.RevokedOn is null && (secret.ExpiresOn is null || secret.ExpiresOn > now))
            .FirstOrDefault(secret => _hasher.Verify(presentedSecret, secret.Secret));
    }

    private async Task RecordAsync(
        Client client, ClientSecret secret, IssuedToken issued, IReadOnlyList<string> scopes, string callerIp, CancellationToken cancellationToken)
    {
        var token = new AccessToken
        {
            Jti = issued.Jti,
            ClientRefId = client.Id,
            ClientSecretId = secret.Id,
            SigningKeyId = issued.SigningKeyId,
            GrantedScopes = string.Join(' ', scopes),
            IssuedOn = issued.IssuedOn,
            ExpiresOn = issued.ExpiresOn,
            IssuedToIp = callerIp
        };

        await _session.BeginTransactionAsync(cancellationToken);
        await _accessTokens.InsertAsync(token, cancellationToken);
        await _clients.MarkSecretUsedAsync(secret.Id, issued.IssuedOn, cancellationToken);
        await _session.CommitAsync(cancellationToken);
    }

    private AuthResult Refuse(string clientId, AuthFailureReason reason)
    {
        _logger.LogWarning("Token request refused for client {ClientId}: {Reason}", Loggable(clientId), reason);
        return AuthResult.Fail(reason);
    }

    /// <summary>Caller-supplied text only reaches the log when it looks like a client ID, so it cannot forge log lines.</summary>
    private static string Loggable(string clientId)
    {
        return clientId.Length <= MaxLoggedClientIdLength && SafeClientId().IsMatch(clientId) ? clientId : "(unrecognised format)";
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeClientId();
}
