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

public sealed class ClientConfigRepository(IDbConnectionFactory connectionFactory) : IClientConfigRepository
{
    public async Task<ClientConfig?> GetByClientRefIdAsync(long clientRefId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ClientConfig>(new CommandDefinition(
            "SELECT * FROM ClientConfigs WHERE clientRefId = @clientRefId",
            new { clientRefId }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddAsync(ClientConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO ClientConfigs
                (clientRefId, authType, baseUrl, tokenUrl, outboundClientId, outboundClientSecretEncrypted, outboundScope,
                 apiTokenEncrypted, tokenExpiresOn, tokenRotatedOn, maxAttempts, isActive, createdOn, createdBy)
            VALUES
                (@ClientRefId, @AuthType, @BaseUrl, @TokenUrl, @OutboundClientId, @OutboundClientSecretEncrypted, @OutboundScope,
                 @ApiTokenEncrypted, @TokenExpiresOn, @TokenRotatedOn, @MaxAttempts, @IsActive, @CreatedOn, @CreatedBy);
            SELECT LAST_INSERT_ID();
            """, config, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(ClientConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ClientConfigs
            SET authType = @AuthType, baseUrl = @BaseUrl, tokenUrl = @TokenUrl, outboundClientId = @OutboundClientId,
                outboundClientSecretEncrypted = @OutboundClientSecretEncrypted, outboundScope = @OutboundScope,
                apiTokenEncrypted = @ApiTokenEncrypted, tokenExpiresOn = @TokenExpiresOn, tokenRotatedOn = @TokenRotatedOn,
                maxAttempts = @MaxAttempts, isActive = @IsActive, modifiedOn = @ModifiedOn, modifiedBy = @ModifiedBy
            WHERE id = @Id
            """, config, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<ClientConfigOutboundUrl>> GetOutboundUrlsAsync(long clientConfigId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClientConfigOutboundUrl>(new CommandDefinition(
            "SELECT * FROM ClientConfigOutboundUrls WHERE clientConfigId = @clientConfigId ORDER BY eventType",
            new { clientConfigId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<ClientConfigOutboundUrl?> GetActiveOutboundUrlAsync(long clientRefId, string eventType, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ClientConfigOutboundUrl>(new CommandDefinition(
            """
            SELECT u.* FROM ClientConfigOutboundUrls u
            JOIN ClientConfigs c ON c.id = u.clientConfigId
            WHERE c.clientRefId = @clientRefId AND u.eventType = @eventType AND c.isActive = 1 AND u.isActive = 1
            ORDER BY u.id
            LIMIT 1
            """, new { clientRefId, eventType }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO ClientConfigOutboundUrls (clientConfigId, eventType, path, method, isActive, createdOn, createdBy)
            VALUES (@ClientConfigId, @EventType, @Path, @Method, @IsActive, @CreatedOn, @CreatedBy);
            SELECT LAST_INSERT_ID();
            """, url, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateOutboundUrlAsync(ClientConfigOutboundUrl url, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ClientConfigOutboundUrls
            SET eventType = @EventType, path = @Path, method = @Method, isActive = @IsActive,
                modifiedOn = @ModifiedOn, modifiedBy = @ModifiedBy
            WHERE id = @Id
            """, url, cancellationToken: cancellationToken)) > 0;
    }
}
