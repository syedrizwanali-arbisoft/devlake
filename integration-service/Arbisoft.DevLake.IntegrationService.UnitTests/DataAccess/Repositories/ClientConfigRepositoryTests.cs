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
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

[Collection(MySqlTestGroup.Name)]
public class ClientConfigRepositoryTests : RepositoryTestBase
{
    public ClientConfigRepositoryTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
    }

    private IClientConfigRepository Repository => Get<IClientConfigRepository>();

    private static ClientConfig NewConfig(long clientRefId) => new()
    {
        ClientRefId = clientRefId,
        AuthType = ClientConfigAuthType.OAuth2ClientCredentials,
        BaseUrl = "https://sophia.example.com",
        TokenUrl = "https://sophia.example.com/oauth2/token/",
        OutboundClientId = "outbound-id",
        OutboundClientSecretEncrypted = "ciphertext",
        MaxAttempts = 3,
        IsActive = true,
        CreatedOn = Instant,
        CreatedBy = "tests"
    };

    [MySqlFact]
    public async Task Config_RoundTripsNullablesAndUpdates()
    {
        var client = await InsertClientAsync();
        var id = await Repository.InsertAsync(NewConfig(client.Id), CancellationToken.None);

        var found = (await Repository.GetByClientRefIdAsync(client.Id, CancellationToken.None))!;
        Assert.Equal(id, found.Id);
        Assert.Equal("ciphertext", found.OutboundClientSecretEncrypted);
        Assert.Null(found.ApiTokenEncrypted);
        Assert.Null(found.TokenExpiresOn);

        var changed = new ClientConfig
        {
            Id = id,
            ClientRefId = client.Id,
            AuthType = ClientConfigAuthType.ApiToken,
            BaseUrl = "https://other.example.com",
            ApiTokenEncrypted = "token-cipher",
            TokenExpiresOn = Instant.AddDays(30),
            MaxAttempts = 5,
            IsActive = false,
            CreatedOn = Instant,
            CreatedBy = "tests",
            ModifiedOn = Instant,
            ModifiedBy = "admin"
        };
        Assert.True(await Repository.UpdateAsync(changed, CancellationToken.None));

        var updated = (await Repository.GetByIdAsync(id, CancellationToken.None))!;
        Assert.Equal(ClientConfigAuthType.ApiToken, updated.AuthType);
        Assert.Equal(5, updated.MaxAttempts);
        Assert.False(updated.IsActive);
        Assert.Equal(Instant.AddDays(30), updated.TokenExpiresOn);
        Assert.Null(updated.OutboundClientSecretEncrypted);
    }

    [MySqlFact]
    public async Task Config_SecondConfigForTheSameClient_IsRejected()
    {
        var client = await InsertClientAsync();
        await Repository.InsertAsync(NewConfig(client.Id), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<MySqlException>(() => Repository.InsertAsync(NewConfig(client.Id), CancellationToken.None));

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [MySqlFact]
    public async Task OutboundUrls_ActiveLookupIgnoresInactiveAndOtherEventTypes()
    {
        var client = await InsertClientAsync();
        var configId = await Repository.InsertAsync(NewConfig(client.Id), CancellationToken.None);
        var inactive = await Repository.InsertOutboundUrlAsync(NewUrl(configId, "engineer-stats", "/old/", isActive: false), CancellationToken.None);
        var active = await Repository.InsertOutboundUrlAsync(NewUrl(configId, "engineer-stats", "/api/v1/devlake/callback/", isActive: true), CancellationToken.None);
        await Repository.InsertOutboundUrlAsync(NewUrl(configId, "engineer-stats-batch", "/batch/", isActive: true), CancellationToken.None);

        var found = await Repository.GetActiveOutboundUrlAsync(configId, "engineer-stats", CancellationToken.None);
        var none = await Repository.GetActiveOutboundUrlAsync(configId, "unknown-event", CancellationToken.None);
        var all = await Repository.GetOutboundUrlsAsync(configId, CancellationToken.None);

        Assert.Equal(active, found!.Id);
        Assert.Equal("POST", found.Method);
        Assert.Null(none);
        Assert.Equal(3, all.Count);

        var stored = (await Repository.GetOutboundUrlByIdAsync(inactive, CancellationToken.None))!;
        Assert.True(await Repository.UpdateOutboundUrlAsync(
            new ClientConfigOutboundUrl { Id = stored.Id, ClientConfigId = configId, EventType = "engineer-stats", Path = "/renamed/", Method = "PUT", IsActive = true, ModifiedOn = Instant, ModifiedBy = "admin" },
            CancellationToken.None));
        Assert.Equal("/renamed/", (await Repository.GetOutboundUrlByIdAsync(inactive, CancellationToken.None))!.Path);
    }

    private static ClientConfigOutboundUrl NewUrl(long configId, string eventType, string path, bool isActive) => new()
    {
        ClientConfigId = configId,
        EventType = eventType,
        Path = path,
        Method = "POST",
        IsActive = isActive,
        CreatedOn = Instant,
        CreatedBy = "tests"
    };
}
