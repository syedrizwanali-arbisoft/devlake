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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Dapper;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

/// <summary>
/// Runs the repositories' SQL against a real MySQL with the real migrations. Each test uses its own client, so the
/// tests don't depend on each other.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class RepositoryIntegrationTests(MySqlDatabaseFixture db)
{
    // MySQL keeps microseconds, so values are rounded to them before they are compared.
    private static readonly DateTime Now = new DateTime(2026, 11, 10, 9, 30, 15, DateTimeKind.Utc).AddTicks(1_234_560); // .123456 s

    private async Task<long> AddClientAsync(string? clientId = null)
    {
        var clients = new ClientRepository(db.ConnectionFactory);
        return await clients.AddAsync(new Client
        {
            ClientId = clientId ?? "client-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test client",
            IsActive = true,
            AccessTokenLifetimeSeconds = 3600,
            CreatedOn = Now,
            CreatedBy = "test",
        });
    }

    [MySqlFact]
    public async Task Migrations_AreIdempotentAndJournaled()
    {
        await db.MigrateAsync();

        await using var connection = await db.ConnectionFactory.OpenAsync();
        var scripts = (await connection.QueryAsync<string>("SELECT scriptname FROM schemaversions ORDER BY scriptname")).ToList();
        Assert.Equal(3, scripts.Count);
        Assert.All(scripts, s => Assert.Contains(".Persistence.Migrations.", s));
    }

    [MySqlFact]
    public async Task Clients_RoundTripWithUtcTimesAndCaseSensitiveIds()
    {
        var clients = new ClientRepository(db.ConnectionFactory);
        var clientId = "case-" + Guid.NewGuid().ToString("N")[..8];
        var id = await AddClientAsync(clientId);

        var loaded = await clients.GetByClientIdAsync(clientId);
        Assert.NotNull(loaded);
        Assert.Equal(id, loaded.Id);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedOn.Kind);
        Assert.Equal(Now, loaded.CreatedOn);
        Assert.Null(await clients.GetByClientIdAsync(clientId.ToUpperInvariant()));

        loaded.Name = "Renamed";
        loaded.IsActive = false;
        loaded.ModifiedOn = Now;
        loaded.ModifiedBy = "admin";
        Assert.True(await clients.UpdateAsync(loaded));
        var updated = await clients.GetByIdAsync(id);
        Assert.Equal(("Renamed", false, "admin"), (updated!.Name, updated.IsActive, updated.ModifiedBy));
        Assert.Contains(await clients.GetAllAsync(), c => c.Id == id);
    }

    [MySqlFact]
    public async Task ClientSecrets_UsableExcludesExpiredAndRevoked()
    {
        var clients = new ClientRepository(db.ConnectionFactory);
        var id = await AddClientAsync();
        var usable = await clients.AddSecretAsync(new ClientSecret { ClientRefId = id, Secret = "h1", ExpiresOn = Now.AddDays(1), CreatedOn = Now, CreatedBy = "t" });
        await clients.AddSecretAsync(new ClientSecret { ClientRefId = id, Secret = "h2", ExpiresOn = Now.AddDays(-1), CreatedOn = Now, CreatedBy = "t" });
        var revoked = await clients.AddSecretAsync(new ClientSecret { ClientRefId = id, Secret = "h3", ExpiresOn = Now.AddDays(1), CreatedOn = Now, CreatedBy = "t" });

        Assert.True(await clients.RevokeSecretAsync(revoked, Now));
        Assert.False(await clients.RevokeSecretAsync(revoked, Now));
        await clients.UpdateSecretLastUsedAsync(usable, Now);

        var secrets = await clients.GetUsableSecretsAsync(id, Now);
        var only = Assert.Single(secrets);
        Assert.Equal(usable, only.Id);
        Assert.Equal(Now, only.LastUsedOn);
        Assert.Equal(3, (await clients.GetSecretsAsync(id)).Count);
    }

    [MySqlFact]
    public async Task Scopes_AssignIsIdempotentAndRemoveWorks()
    {
        var scopes = new ScopeRepository(db.ConnectionFactory);
        var client = await AddClientAsync();
        var name = "scope:" + Guid.NewGuid().ToString("N")[..8];
        var scope = await scopes.AddAsync(new Scope { Name = name, Description = "d", CreatedOn = Now });
        var assignment = new ClientScope { ClientRefId = client, ScopeId = scope, CreatedOn = Now, CreatedBy = "t" };

        await scopes.AssignToClientAsync(assignment);
        await scopes.AssignToClientAsync(assignment);
        Assert.Equal([name], await scopes.GetNamesForClientAsync(client));

        var loaded = await scopes.GetByIdAsync(scope);
        loaded!.Description = "updated";
        Assert.True(await scopes.UpdateAsync(loaded));
        Assert.Contains(await scopes.GetAllAsync(), s => s.Id == scope && s.Description == "updated");

        await scopes.RemoveFromClientAsync(client, scope);
        Assert.Empty(await scopes.GetNamesForClientAsync(client));
    }

    [MySqlFact]
    public async Task SigningKeys_ActiveIsTheNewestNotRetiredOrFutureKey()
    {
        var keys = new SigningKeyRepository(db.ConnectionFactory);
        await using (var connection = await db.ConnectionFactory.OpenAsync())
        {
            // Earlier tests may have added keys; this one needs a clean set to know which key is active.
            await connection.ExecuteAsync("DELETE FROM AccessTokens; DELETE FROM SigningKeys;");
        }

        var older = await keys.AddAsync(new SigningKey { Kid = "k-old", Algorithm = "RS256", PublicKey = "p", PrivateKeyRef = "r1", ActivatedOn = Now.AddDays(-2), CreatedOn = Now });
        var newer = await keys.AddAsync(new SigningKey { Kid = "k-new", Algorithm = "RS256", PublicKey = "p", PrivateKeyRef = "r2", ActivatedOn = Now.AddDays(-1), CreatedOn = Now });
        await keys.AddAsync(new SigningKey { Kid = "k-future", Algorithm = "RS256", PublicKey = "p", PrivateKeyRef = "r3", ActivatedOn = Now.AddDays(1), CreatedOn = Now });

        Assert.Equal("k-new", (await keys.GetActiveAsync(Now))?.Kid);
        Assert.True(await keys.RetireAsync(newer, Now.AddSeconds(-1)));
        Assert.Equal(older, (await keys.GetActiveAsync(Now))?.Id);
        Assert.Equal(3, (await keys.GetAllAsync()).Count);
    }

    [MySqlFact]
    public async Task AccessTokens_RevokeOnlyOnce()
    {
        var clients = new ClientRepository(db.ConnectionFactory);
        var tokens = new AccessTokenRepository(db.ConnectionFactory);
        var keys = new SigningKeyRepository(db.ConnectionFactory);
        var client = await AddClientAsync();
        var secret = await clients.AddSecretAsync(new ClientSecret { ClientRefId = client, Secret = "h", ExpiresOn = Now.AddDays(1), CreatedOn = Now, CreatedBy = "t" });
        var key = await keys.AddAsync(new SigningKey { Kid = "k-" + Guid.NewGuid().ToString("N")[..8], Algorithm = "RS256", PublicKey = "p", PrivateKeyRef = "r", ActivatedOn = Now, CreatedOn = Now });
        var jti = Guid.NewGuid().ToString();

        await tokens.AddAsync(new AccessToken { Jti = jti, ClientRefId = client, ClientSecretId = secret, SigningKeyId = key, GrantedScopes = "stats:read", IssuedOn = Now, ExpiresOn = Now.AddHours(1), IssuedToIp = "::1" });

        Assert.Equal("::1", (await tokens.GetByJtiAsync(jti))?.IssuedToIp);
        Assert.True(await tokens.RevokeAsync(jti, "leaked", Now));
        Assert.False(await tokens.RevokeAsync(jti, "again", Now));
        Assert.Equal("leaked", (await tokens.GetByJtiAsync(jti))?.RevokedReason);
        Assert.Contains(await tokens.GetRecentAsync(500), t => t.Jti == jti);
    }

    [MySqlFact]
    public async Task RequestLogs_ReadCorrelationIdsAsStringsAndSkipRevokedDuplicates()
    {
        var logs = new RequestLogRepository(db.ConnectionFactory);
        var client = await AddClientAsync();
        var revokedId = Guid.NewGuid().ToString();
        var queuedId = Guid.NewGuid().ToString();
        RequestLog Log(string correlationId, string status) => new()
        {
            ClientRefId = client, CorrelationId = correlationId, Url = "/u", Status = status, EventType = "engineer-stats",
            RequestHash = "same-hash", CreatedOn = Now,
        };

        await logs.AddAsync(Log(revokedId, RequestLogStatus.RevokedNoCallbackUrl), "{\"a\": 1}");
        Assert.Null(await logs.FindRecentDuplicateAsync(client, "same-hash", Now.AddMinutes(-5)));

        var id = await logs.AddAsync(Log(queuedId, RequestLogStatus.Queued), "{\"a\": 2}");
        // Regression: correlationId was CHAR(36), which MySqlConnector reads as a Guid, so reading it into a string threw.
        Assert.Equal(queuedId, (await logs.GetByCorrelationIdAsync(queuedId))?.CorrelationId);
        Assert.Equal(queuedId, (await logs.FindRecentDuplicateAsync(client, "same-hash", Now.AddMinutes(-5)))?.CorrelationId);
        Assert.Null(await logs.FindRecentDuplicateAsync(client, "same-hash", Now.AddMinutes(1)));
        Assert.Contains("\"a\"", await logs.GetJsonAsync(id));

        Assert.True(await logs.UpdateStatusAsync(id, RequestLogStatus.Success));
        Assert.Equal(RequestLogStatus.Success, (await logs.GetByIdAsync(id))?.Status);

        await logs.AddAttemptAsync(new RequestResponseAttempt { RequestLogId = id, ResponseJson = "{}", AttemptStatus = "Failed", FailureError = "502", AttemptBy = "job", AttemptOn = Now });
        Assert.Equal("502", Assert.Single(await logs.GetAttemptsAsync(id)).FailureError);

        var (page, total) = await logs.SearchAsync(client, RequestLogStatus.Success, Now.AddDays(-1), Now.AddDays(1), 10, 0);
        Assert.Equal(1, total);
        Assert.Equal(id, Assert.Single(page).Id);
        var (none, zero) = await logs.SearchAsync(client, RequestLogStatus.Failed, null, null, 10, 0);
        Assert.Empty(none);
        Assert.Equal(0, zero);
    }

    [MySqlFact]
    public async Task RequestLogs_RejectABodyThatIsNotJson()
    {
        var logs = new RequestLogRepository(db.ConnectionFactory);
        var client = await AddClientAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => logs.AddAsync(new RequestLog
        {
            ClientRefId = client, CorrelationId = Guid.NewGuid().ToString(), Url = "/u", Status = "Received", EventType = "e", CreatedOn = Now,
        }, "not json"));
    }

    [MySqlFact]
    public async Task ClientConfigs_OnePerClientAndOnlyActiveUrlsResolve()
    {
        var configs = new ClientConfigRepository(db.ConnectionFactory);
        var client = await AddClientAsync();
        var configId = await configs.AddAsync(new ClientConfig
        {
            ClientRefId = client, AuthType = "oauth2", BaseUrl = "https://x", TokenUrl = "/t", OutboundClientId = "o",
            OutboundClientSecretEncrypted = "v1:e", MaxAttempts = 3, IsActive = true, CreatedOn = Now, CreatedBy = "t",
        });
        await Assert.ThrowsAnyAsync<Exception>(() => configs.AddAsync(new ClientConfig
        {
            ClientRefId = client, AuthType = "oauth2", BaseUrl = "https://y", MaxAttempts = 1, CreatedOn = Now, CreatedBy = "t",
        }));

        var urlId = await configs.AddOutboundUrlAsync(new ClientConfigOutboundUrl
        {
            ClientConfigId = configId, EventType = "engineer-stats", Path = "/cb", Method = "POST", IsActive = true, CreatedOn = Now, CreatedBy = "t",
        });
        Assert.Equal(urlId, (await configs.GetActiveOutboundUrlAsync(client, "engineer-stats"))?.Id);
        Assert.Null(await configs.GetActiveOutboundUrlAsync(client, "engineer-stats-batch"));

        var config = await configs.GetByClientRefIdAsync(client);
        config!.IsActive = false;
        config.ModifiedOn = Now;
        config.ModifiedBy = "admin";
        Assert.True(await configs.UpdateAsync(config));
        Assert.Null(await configs.GetActiveOutboundUrlAsync(client, "engineer-stats"));

        var url = Assert.Single(await configs.GetOutboundUrlsAsync(configId));
        url.Path = "/cb2";
        url.ModifiedOn = Now;
        url.ModifiedBy = "admin";
        Assert.True(await configs.UpdateOutboundUrlAsync(url));
        Assert.Equal("/cb2", Assert.Single(await configs.GetOutboundUrlsAsync(configId)).Path);
    }

    [MySqlFact]
    public async Task EmailMappings_MatchTheArbisoftEmailIgnoringCase()
    {
        var mappings = new EmailMappingRepository(db.ConnectionFactory);
        var arbisoft = "eng" + Guid.NewGuid().ToString("N")[..6] + "@arbisoft.com";
        var id = await mappings.AddAsync(new EmailMapping { ArbisoftEmail = arbisoft, Email = "a@gmail.com", CreatedOn = Now });
        await mappings.AddAsync(new EmailMapping { ArbisoftEmail = arbisoft, Email = "a@outlook.com", CreatedOn = Now });

        Assert.Equal(2, (await mappings.GetMappedEmailsAsync(arbisoft.ToUpperInvariant())).Count);
        var loaded = await mappings.GetByIdAsync(id);
        loaded!.Email = "b@gmail.com";
        Assert.True(await mappings.UpdateAsync(loaded));
        Assert.Contains(await mappings.GetAllAsync(), m => m.Id == id && m.Email == "b@gmail.com");
    }

    [MySqlFact]
    public async Task SecretStore_KeepsEachReferenceOnce()
    {
        var store = new SecretStoreRepository(db.ConnectionFactory);
        var reference = "signing-key-" + Guid.NewGuid().ToString("N")[..8];

        await store.AddAsync(new SecretStoreEntry { SecretRef = reference, ValueEncrypted = "v1:x", CreatedOn = Now, CreatedBy = "admin" });

        Assert.Equal("v1:x", (await store.GetAsync(reference))?.ValueEncrypted);
        Assert.Null(await store.GetAsync(reference.ToUpperInvariant()));
        await Assert.ThrowsAnyAsync<Exception>(() => store.AddAsync(new SecretStoreEntry { SecretRef = reference, ValueEncrypted = "v1:y", CreatedOn = Now, CreatedBy = "admin" }));
    }
}
