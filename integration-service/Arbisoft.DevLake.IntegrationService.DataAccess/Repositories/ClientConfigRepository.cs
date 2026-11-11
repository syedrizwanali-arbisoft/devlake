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

public sealed class ClientConfigRepository : RepositoryBase, IClientConfigRepository
{
    private const string Columns =
        "id, clientRefId, authType, baseUrl, tokenUrl, outboundClientId, outboundClientSecretEncrypted, outboundScope, apiTokenEncrypted, " +
        "tokenExpiresOn, tokenRotatedOn, maxAttempts, isActive, createdOn, createdBy, modifiedOn, modifiedBy";

    private const string UrlColumns = "id, clientConfigId, eventType, path, method, isActive, createdOn, createdBy, modifiedOn, modifiedBy";

    public ClientConfigRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<ClientConfig?> GetByClientRefIdAsync(long clientRefId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ClientConfig>($"SELECT {Columns} FROM ClientConfigs WHERE clientRefId = @clientRefId", new { clientRefId }, cancellationToken);
    }

    public Task<ClientConfig?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ClientConfig>($"SELECT {Columns} FROM ClientConfigs WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<PagedResult<ClientConfig>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return QueryPageAsync<ClientConfig>(Columns, "FROM ClientConfigs", "id", null, page, pageSize, cancellationToken);
    }

    public Task<long> InsertAsync(ClientConfig config, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO ClientConfigs (clientRefId, authType, baseUrl, tokenUrl, outboundClientId, outboundClientSecretEncrypted, outboundScope, " +
            "apiTokenEncrypted, tokenExpiresOn, tokenRotatedOn, maxAttempts, isActive, createdOn, createdBy, modifiedOn, modifiedBy) " +
            "VALUES (@ClientRefId, @AuthType, @BaseUrl, @TokenUrl, @OutboundClientId, @OutboundClientSecretEncrypted, @OutboundScope, " +
            "@ApiTokenEncrypted, @TokenExpiresOn, @TokenRotatedOn, @MaxAttempts, @IsActive, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy)",
            config,
            cancellationToken);
    }

    public async Task<bool> UpdateAsync(ClientConfig config, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync(
            "UPDATE ClientConfigs SET authType = @AuthType, baseUrl = @BaseUrl, tokenUrl = @TokenUrl, outboundClientId = @OutboundClientId, " +
            "outboundClientSecretEncrypted = @OutboundClientSecretEncrypted, outboundScope = @OutboundScope, apiTokenEncrypted = @ApiTokenEncrypted, " +
            "tokenExpiresOn = @TokenExpiresOn, tokenRotatedOn = @TokenRotatedOn, maxAttempts = @MaxAttempts, isActive = @IsActive, " +
            "modifiedOn = @ModifiedOn, modifiedBy = @ModifiedBy WHERE id = @Id",
            config,
            cancellationToken);
        return changed > 0;
    }

    public Task<IReadOnlyList<ClientConfigOutboundUrl>> GetOutboundUrlsAsync(long clientConfigId, CancellationToken cancellationToken)
    {
        return QueryAsync<ClientConfigOutboundUrl>($"SELECT {UrlColumns} FROM ClientConfigOutboundUrls WHERE clientConfigId = @clientConfigId ORDER BY eventType, id", new { clientConfigId }, cancellationToken);
    }

    public Task<ClientConfigOutboundUrl?> GetOutboundUrlByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ClientConfigOutboundUrl>($"SELECT {UrlColumns} FROM ClientConfigOutboundUrls WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<ClientConfigOutboundUrl?> GetActiveOutboundUrlAsync(long clientConfigId, string eventType, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ClientConfigOutboundUrl>(
            $"SELECT {UrlColumns} FROM ClientConfigOutboundUrls WHERE clientConfigId = @clientConfigId AND eventType = @eventType AND isActive = 1 ORDER BY id DESC LIMIT 1",
            new { clientConfigId, eventType },
            cancellationToken);
    }

    public Task<long> InsertOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO ClientConfigOutboundUrls (clientConfigId, eventType, path, method, isActive, createdOn, createdBy, modifiedOn, modifiedBy) " +
            "VALUES (@ClientConfigId, @EventType, @Path, @Method, @IsActive, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy)",
            url,
            cancellationToken);
    }

    public async Task<bool> UpdateOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync(
            "UPDATE ClientConfigOutboundUrls SET eventType = @EventType, path = @Path, method = @Method, isActive = @IsActive, " +
            "modifiedOn = @ModifiedOn, modifiedBy = @ModifiedBy WHERE id = @Id",
            url,
            cancellationToken);
        return changed > 0;
    }
}
