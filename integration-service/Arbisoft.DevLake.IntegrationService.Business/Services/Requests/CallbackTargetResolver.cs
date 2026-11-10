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

using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class CallbackTargetResolver(IClientConfigRepository clientConfigs, ISecretEncryptor secretEncryptor)
    : ICallbackTargetResolver
{
    public async Task<CallbackTarget?> ResolveAsync(long clientRefId, string eventType, CancellationToken cancellationToken = default)
    {
        var config = await clientConfigs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        if (config is not { IsActive: true })
        {
            return null;
        }

        var url = await clientConfigs.GetActiveOutboundUrlAsync(clientRefId, eventType, cancellationToken);
        if (url is null)
        {
            return null;
        }

        var connection = new SophiaConnection(config.Id, config.BaseUrl, BuildAuth(config));
        return new CallbackTarget(connection, url.Method, url.Path, Math.Max(config.MaxAttempts, 1));
    }

    private OutboundAuth BuildAuth(ClientConfig config) => config.AuthType switch
    {
        ClientConfigAuthTypes.OAuth2ClientCredentials => new OAuth2ClientCredentials(
            Require(config.TokenUrl, config, "tokenUrl"),
            Require(config.OutboundClientId, config, "outboundClientId"),
            secretEncryptor.Decrypt(Require(config.OutboundClientSecretEncrypted, config, "outboundClientSecretEncrypted")),
            config.OutboundScope),
        ClientConfigAuthTypes.ApiToken => new StaticApiToken(
            secretEncryptor.Decrypt(Require(config.ApiTokenEncrypted, config, "apiTokenEncrypted"))),
        _ => throw new InvalidOperationException($"Client config {config.Id} has unknown auth type '{config.AuthType}'."),
    };

    private static string Require(string? value, ClientConfig config, string column) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Client config {config.Id} uses auth type '{config.AuthType}' but has no {column}.");
}
