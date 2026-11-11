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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>A client created by <see cref="ApiSeed"/>.</summary>
public sealed record SeededClient(string ClientId, long ClientRefId, long SecretId);

/// <summary>What to put in a hand-made token. Anything left out is a valid value, so a test changes exactly one thing.</summary>
public sealed record MintSpec(
    string? Kid = null,
    string? Issuer = null,
    string? Audience = null,
    DateTimeOffset? ExpiresOn = null,
    string Scope = "stats:read",
    string? Jti = null,
    SecurityKey? SigningKey = null,
    string Algorithm = RsaKeyPair.Algorithm);

/// <summary>Creates clients, signing keys and tokens in the test database through the API's own services, for end-to-end tests.</summary>
public sealed class ApiSeed
{
    public const string Secret = "correct-horse-battery-staple";

    private readonly ApiFactory _factory;

    public ApiSeed(ApiFactory factory)
    {
        _factory = factory;
    }

    public RsaKeyPair KeyPair { get; private set; } = RsaKeyPair.Generate();

    public string Kid { get; private set; } = string.Empty;

    public long SigningKeyId { get; private set; }

    /// <summary>Retires every active key, then activates a new one. Each test starts with exactly one active signing key.</summary>
    public async Task SeedSigningKeyAsync()
    {
        await RetireAllSigningKeysAsync();
        await AddActiveSigningKeyAsync();
    }

    /// <summary>Retires the current key and activates a new one, as the admin portal's rotate action will.</summary>
    public Task RotateSigningKeyAsync() => SeedSigningKeyAsync();

    public async Task RetireAllSigningKeysAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await using var connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync("UPDATE SigningKeys SET retiredOn = UTC_TIMESTAMP(6) WHERE retiredOn IS NULL");
    }

    public async Task<SeededClient> SeedClientAsync(bool active = true, string[]? scopes = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
        var scopeRepository = scope.ServiceProvider.GetRequiredService<IScopeRepository>();
        var now = DateTimeOffset.UtcNow;

        var clientId = $"client-{Guid.NewGuid():N}";
        var clientRefId = await clients.InsertAsync(
            new Client { ClientId = clientId, Name = "Test", IsActive = active, AccessTokenLifetimeSeconds = 900, CreatedOn = now, CreatedBy = "tests" },
            CancellationToken.None);
        var secretId = await clients.InsertSecretAsync(
            new ClientSecret
            {
                ClientRefId = clientRefId,
                Secret = scope.ServiceProvider.GetRequiredService<IClientSecretHasher>().Hash(Secret),
                CreatedOn = now,
                CreatedBy = "tests"
            },
            CancellationToken.None);

        var scopeIds = new List<long>();
        foreach (var name in scopes ?? [ScopeNames.StatsRead, ScopeNames.ProjectsFetch])
        {
            var found = await scopeRepository.GetByNameAsync(name, CancellationToken.None);
            scopeIds.Add(found?.Id ?? await scopeRepository.InsertAsync(new Scope { Name = name, CreatedOn = now }, CancellationToken.None));
        }

        await clients.ReplaceScopesAsync(clientRefId, scopeIds, "tests", now, CancellationToken.None);
        return new SeededClient(clientId, clientRefId, secretId);
    }

    /// <summary>Gives a client an active outbound config with an active callback URL for each event type, as the admin portal will.</summary>
    public Task SeedCallbackAsync(SeededClient client, params string[] eventTypes) => SeedSophiaAsync(client, maxAttempts: 3, eventTypes);

    /// <summary>
    /// Gives a client a complete Sophia setup: OAuth2 client credentials with an encrypted secret, and a callback URL for each event type.
    /// The stub in <see cref="UpstreamStub"/> answers for <c>https://sophia.test</c>.
    /// </summary>
    public async Task SeedSophiaAsync(SeededClient client, int maxAttempts, params string[] eventTypes)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var configs = scope.ServiceProvider.GetRequiredService<IClientConfigRepository>();
        var now = DateTimeOffset.UtcNow;
        var configId = await configs.InsertAsync(
            new ClientConfig
            {
                ClientRefId = client.ClientRefId,
                AuthType = ClientConfigAuthType.OAuth2ClientCredentials,
                BaseUrl = "https://sophia.test",
                TokenUrl = "https://sophia.test/oauth2/token/",
                OutboundClientId = "stage-client-id",
                OutboundClientSecretEncrypted = scope.ServiceProvider.GetRequiredService<ISecretEncryptor>().Encrypt("stage-client-secret"),
                MaxAttempts = maxAttempts,
                IsActive = true,
                CreatedOn = now,
                CreatedBy = "tests"
            },
            CancellationToken.None);
        foreach (var eventType in eventTypes)
        {
            await configs.InsertOutboundUrlAsync(
                new ClientConfigOutboundUrl { ClientConfigId = configId, EventType = eventType, Path = "/api/v1/devlake/callback/", Method = "POST", IsActive = true, CreatedOn = now, CreatedBy = "tests" },
                CancellationToken.None);
        }
    }

    /// <summary>Puts a request log with the given status straight into the database and returns its correlation ID.</summary>
    public async Task<string> SeedRequestAsync(SeededClient client, string status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var correlationId = Guid.NewGuid().ToString("D");
        await scope.ServiceProvider.GetRequiredService<IRequestLogRepository>().InsertAsync(
            new RequestLog
            {
                ClientRefId = client.ClientRefId,
                CorrelationId = correlationId,
                Url = "/api/v1/engineers/{email}/stats?tf={timeFrame}",
                Status = status,
                EventType = "engineer-stats",
                CreatedOn = DateTimeOffset.UtcNow
            },
            CancellationToken.None);
        return correlationId;
    }

    /// <summary>
    /// Turns off every other client's outbound config. The test database is shared, so other tests leave clients behind whose secrets were encrypted
    /// with a different key; a processing run must not trip over them.
    /// </summary>
    public Task IsolateAsync(SeededClient client) => ExecuteAsync("UPDATE ClientConfigs SET isActive = 0 WHERE clientRefId <> @id", new { id = client.ClientRefId });

    /// <summary>Maps an engineer's Arbisoft email to another email they commit with.</summary>
    public async Task SeedEmailMappingAsync(string arbisoftEmail, string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailMappingRepository>().InsertAsync(
            new EmailMapping { ArbisoftEmail = arbisoftEmail.ToLowerInvariant(), Email = email.ToLowerInvariant(), EmailAsReceived = email, CreatedOn = DateTimeOffset.UtcNow }, CancellationToken.None);
    }

    /// <summary>Turns a client's callback URL for an event type off, as an administrator would.</summary>
    public async Task DeactivateCallbackUrlAsync(SeededClient client, string eventType)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var configs = scope.ServiceProvider.GetRequiredService<IClientConfigRepository>();
        var config = (await configs.GetByClientRefIdAsync(client.ClientRefId, CancellationToken.None))!;
        foreach (var url in await configs.GetOutboundUrlsAsync(config.Id, CancellationToken.None))
        {
            if (url.EventType == eventType)
            {
                await configs.UpdateOutboundUrlAsync(
                    new ClientConfigOutboundUrl { Id = url.Id, ClientConfigId = url.ClientConfigId, EventType = url.EventType, Path = url.Path, Method = url.Method, IsActive = false, ModifiedOn = DateTimeOffset.UtcNow, ModifiedBy = "tests" },
                    CancellationToken.None);
            }
        }
    }

    /// <summary>Every attempt recorded for a request, oldest first.</summary>
    public async Task<IReadOnlyList<RequestResponseAttempt>> AttemptsAsync(long requestLogId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRequestLogRepository>().GetAttemptsAsync(requestLogId, CancellationToken.None);
    }

    /// <summary>How many messages are waiting in this API instance's queue.</summary>
    public async Task<int> QueueDepthAsync()
    {
        return await _factory.Services.GetRequiredService<IRequestQueueConsumer>().GetDepthAsync(CancellationToken.None);
    }

    /// <summary>Runs a statement straight on the test database, for setting up a state a test needs, such as an old request.</summary>
    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await using var connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(sql, parameters);
    }

    public async Task<RequestLog?> LogAsync(string correlationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRequestLogRepository>().GetByCorrelationIdAsync(correlationId, CancellationToken.None);
    }

    public async Task<string?> LogJsonAsync(long requestLogId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRequestLogRepository>().GetJsonAsync(requestLogId, CancellationToken.None);
    }

    public async Task<IReadOnlyList<RequestLog>> LogsForAsync(SeededClient client)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var page = await scope.ServiceProvider.GetRequiredService<IRequestLogRepository>().ListAsync(new RequestLogFilter(client.ClientRefId), 1, 100, CancellationToken.None);
        return page.Items;
    }

    public async Task DeactivateClientAsync(SeededClient client)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
        var current = (await clients.GetByIdAsync(client.ClientRefId, CancellationToken.None))!;
        await clients.UpdateAsync(
            new Client
            {
                Id = current.Id,
                ClientId = current.ClientId,
                Name = current.Name,
                IsActive = false,
                AccessTokenLifetimeSeconds = current.AccessTokenLifetimeSeconds,
                CreatedOn = current.CreatedOn,
                CreatedBy = current.CreatedBy,
                ModifiedOn = DateTimeOffset.UtcNow,
                ModifiedBy = "tests"
            },
            CancellationToken.None);
    }

    /// <summary>Gets a real token from POST /auth/token/.</summary>
    public static async Task<string> RequestTokenAsync(HttpClient http, SeededClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.ClientId}:{Secret}")));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    /// <summary>Makes a token with the current key, and records it as issued so only the changed property can make it fail.</summary>
    public async Task<string> MintAndRecordAsync(SeededClient client, MintSpec? spec = null)
    {
        spec ??= new MintSpec();
        var jti = spec.Jti ?? Guid.NewGuid().ToString("D");
        var expiresOn = spec.ExpiresOn ?? DateTimeOffset.UtcNow.AddMinutes(10);
        var token = Mint(spec with { Jti = jti, ExpiresOn = expiresOn });

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAccessTokenRepository>().InsertAsync(
            new AccessToken
            {
                Jti = jti,
                ClientRefId = client.ClientRefId,
                ClientSecretId = client.SecretId,
                SigningKeyId = SigningKeyId,
                GrantedScopes = spec.Scope,
                IssuedOn = expiresOn.AddMinutes(-15),
                ExpiresOn = expiresOn,
                IssuedToIp = "unknown"
            },
            CancellationToken.None);
        return token;
    }

    /// <summary>Makes a token without recording it, for tokens the Auth API never issued.</summary>
    public string Mint(MintSpec spec)
    {
        var expiresOn = spec.ExpiresOn ?? DateTimeOffset.UtcNow.AddMinutes(10);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(KeyPair.PrivateKeyPem);
        var key = spec.SigningKey ?? new RsaSecurityKey(rsa.ExportParameters(true));
        key.KeyId = spec.Kid ?? Kid;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer ?? SecurityTestData.Issuer,
            Audience = spec.Audience ?? SecurityTestData.Audience,
            IssuedAt = expiresOn.AddMinutes(-15).UtcDateTime,
            NotBefore = expiresOn.AddMinutes(-15).UtcDateTime,
            Expires = expiresOn.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = spec.Jti ?? Guid.NewGuid().ToString("D"),
                [TokenClaims.Client] = "minted",
                [TokenClaims.Scope] = spec.Scope
            },
            SigningCredentials = new SigningCredentials(key, spec.Algorithm)
        });
    }

    public async Task RevokeTokenAsync(string jti)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenRepository>();
        var stored = await tokens.GetByJtiAsync(jti, CancellationToken.None);
        await tokens.RevokeAsync(stored!.Id, "revoked by test", DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task AddActiveSigningKeyAsync()
    {
        KeyPair = RsaKeyPair.Generate();
        Kid = $"kid-{Guid.NewGuid():N}";
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISecretStore>().SaveAsync($"ref-{Kid}", KeyPair.PrivateKeyPem, CancellationToken.None);
        SigningKeyId = await scope.ServiceProvider.GetRequiredService<ISigningKeyRepository>().InsertAsync(
            new SigningKey
            {
                Kid = Kid,
                Algorithm = RsaKeyPair.Algorithm,
                PublicKey = KeyPair.PublicKeyPem,
                PrivateKeyRef = $"ref-{Kid}",
                ActivatedOn = DateTimeOffset.UtcNow.AddSeconds(-5),
                CreatedOn = DateTimeOffset.UtcNow
            },
            CancellationToken.None);
    }
}
