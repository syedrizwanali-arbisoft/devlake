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
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IClientConfigService
{
    Task<ClientConfigDto?> GetAsync(long clientRefId, CancellationToken cancellationToken = default);

    /// <summary>Creates the client's config, or updates it if one exists. A client has at most one config.</summary>
    Task<AdminResult> SaveAsync(long clientRefId, ClientConfigInput input, string changedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientConfigOutboundUrlDto>> ListOutboundUrlsAsync(long clientRefId, CancellationToken cancellationToken = default);
    Task<AdminResult<long>> AddOutboundUrlAsync(long clientRefId, OutboundUrlInput input, string createdBy, CancellationToken cancellationToken = default);
    Task<AdminResult> UpdateOutboundUrlAsync(long clientRefId, long urlId, OutboundUrlInput input, string modifiedBy, CancellationToken cancellationToken = default);
}

public sealed class ClientConfigService(
    IClientConfigRepository configs,
    IClientRepository clients,
    ISecretEncryptor encryptor,
    TimeProvider timeProvider) : IClientConfigService
{
    public const int MaxAttemptsLimit = 10;
    public static readonly IReadOnlyList<string> AuthTypes = [ClientConfigAuthTypes.OAuth2ClientCredentials, ClientConfigAuthTypes.ApiToken];
    public static readonly IReadOnlyList<string> Methods = ["POST", "PUT", "PATCH"];

    public async Task<ClientConfigDto?> GetAsync(long clientRefId, CancellationToken cancellationToken = default) =>
        await configs.GetByClientRefIdAsync(clientRefId, cancellationToken) is { } config ? ToDto(config) : null;

    public async Task<AdminResult> SaveAsync(long clientRefId, ClientConfigInput input, string changedBy, CancellationToken cancellationToken = default)
    {
        if (await clients.GetByIdAsync(clientRefId, cancellationToken) is null)
        {
            return AdminResult.Fail("The client was not found.");
        }

        var existing = await configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        var config = existing ?? new ClientConfig { ClientRefId = clientRefId, CreatedBy = changedBy };
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!AuthTypes.Contains(input.AuthType))
        {
            return AdminResult.Fail("Choose an auth type.");
        }

        var baseUrl = input.BaseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
        {
            return AdminResult.Fail("The base URL must be an absolute http or https URL.");
        }

        if (input.MaxAttempts is < 1 or > MaxAttemptsLimit)
        {
            return AdminResult.Fail($"Max attempts must be 1 to {MaxAttemptsLimit}.");
        }

        var newSecret = Blank(input.OutboundClientSecret);
        var newToken = Blank(input.ApiToken);
        var rotated = false;
        if (input.AuthType == ClientConfigAuthTypes.OAuth2ClientCredentials)
        {
            var tokenUrl = Blank(input.TokenUrl);
            var clientId = Blank(input.OutboundClientId);
            if (tokenUrl is null || clientId is null)
            {
                return AdminResult.Fail("OAuth2 needs a token URL and an outbound client ID.");
            }

            if (newSecret is null && string.IsNullOrEmpty(config.OutboundClientSecretEncrypted))
            {
                return AdminResult.Fail("OAuth2 needs an outbound client secret.");
            }

            config.TokenUrl = tokenUrl;
            config.OutboundClientId = clientId;
            config.OutboundScope = Blank(input.OutboundScope);
            if (newSecret is not null)
            {
                config.OutboundClientSecretEncrypted = encryptor.Encrypt(newSecret);
                rotated = true;
            }

            // The other auth type's credential is removed, so no unused secret is left behind.
            config.ApiTokenEncrypted = null;
        }
        else
        {
            if (newToken is null && string.IsNullOrEmpty(config.ApiTokenEncrypted))
            {
                return AdminResult.Fail("A static token config needs an API token.");
            }

            if (newToken is not null)
            {
                config.ApiTokenEncrypted = encryptor.Encrypt(newToken);
                rotated = true;
            }

            config.TokenUrl = null;
            config.OutboundClientId = null;
            config.OutboundClientSecretEncrypted = null;
            config.OutboundScope = null;
        }

        config.AuthType = input.AuthType;
        config.BaseUrl = baseUrl;
        config.MaxAttempts = input.MaxAttempts;
        config.IsActive = input.IsActive;
        config.TokenExpiresOn = input.TokenExpiresOn is { } expires ? DateTime.SpecifyKind(expires, DateTimeKind.Utc) : null;
        if (rotated)
        {
            config.TokenRotatedOn = now;
        }

        if (existing is null)
        {
            config.CreatedOn = now;
            await configs.AddAsync(config, cancellationToken);
        }
        else
        {
            config.ModifiedOn = now;
            config.ModifiedBy = changedBy;
            await configs.UpdateAsync(config, cancellationToken);
        }

        return AdminResult.Ok;
    }

    public async Task<IReadOnlyList<ClientConfigOutboundUrlDto>> ListOutboundUrlsAsync(long clientRefId, CancellationToken cancellationToken = default)
    {
        var config = await configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        return config is null ? [] : (await configs.GetOutboundUrlsAsync(config.Id, cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<AdminResult<long>> AddOutboundUrlAsync(
        long clientRefId, OutboundUrlInput input, string createdBy, CancellationToken cancellationToken = default)
    {
        var config = await configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        if (config is null)
        {
            return AdminResult<long>.Fail("Save the client's outbound config first.");
        }

        var error = await ValidateUrlAsync(config.Id, null, input, cancellationToken);
        if (error is not null)
        {
            return AdminResult<long>.Fail(error);
        }

        var id = await configs.AddOutboundUrlAsync(new ClientConfigOutboundUrl
        {
            ClientConfigId = config.Id,
            EventType = input.EventType.Trim(),
            Method = input.Method,
            Path = input.Path.Trim(),
            IsActive = input.IsActive,
            CreatedOn = timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = createdBy,
        }, cancellationToken);
        return AdminResult<long>.Ok(id);
    }

    public async Task<AdminResult> UpdateOutboundUrlAsync(
        long clientRefId, long urlId, OutboundUrlInput input, string modifiedBy, CancellationToken cancellationToken = default)
    {
        var config = await configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        var url = config is null ? null : (await configs.GetOutboundUrlsAsync(config.Id, cancellationToken)).FirstOrDefault(u => u.Id == urlId);
        if (config is null || url is null)
        {
            return AdminResult.Fail("The outbound URL was not found for this client.");
        }

        var error = await ValidateUrlAsync(config.Id, urlId, input, cancellationToken);
        if (error is not null)
        {
            return AdminResult.Fail(error);
        }

        url.EventType = input.EventType.Trim();
        url.Method = input.Method;
        url.Path = input.Path.Trim();
        url.IsActive = input.IsActive;
        url.ModifiedOn = timeProvider.GetUtcNow().UtcDateTime;
        url.ModifiedBy = modifiedBy;
        await configs.UpdateOutboundUrlAsync(url, cancellationToken);
        return AdminResult.Ok;
    }

    private async Task<string?> ValidateUrlAsync(long configId, long? urlId, OutboundUrlInput input, CancellationToken cancellationToken)
    {
        var eventType = input.EventType?.Trim() ?? string.Empty;
        if (eventType.Length is 0 or > 128)
        {
            return "The event type is required and can be at most 128 characters.";
        }

        if (!Methods.Contains(input.Method))
        {
            return "The method must be POST, PUT or PATCH.";
        }

        var path = input.Path?.Trim() ?? string.Empty;
        if (path.Length is 0 or > 2048 || Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https"
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            return "The path must be relative to the config's base URL, for example /api/v1/devlake/callback/.";
        }

        // The processor uses the first active URL for an event type, so a second active one would silently never be used.
        if (input.IsActive && (await configs.GetOutboundUrlsAsync(configId, cancellationToken))
                .Any(u => u.Id != urlId && u.IsActive && u.EventType == eventType))
        {
            return $"There is already an active URL for '{eventType}'. Deactivate it first.";
        }

        return null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ClientConfigDto ToDto(ClientConfig c) => new(
        c.Id, c.ClientRefId, c.AuthType, c.BaseUrl, c.TokenUrl, c.OutboundClientId, !string.IsNullOrEmpty(c.OutboundClientSecretEncrypted),
        c.OutboundScope, !string.IsNullOrEmpty(c.ApiTokenEncrypted), c.TokenExpiresOn, c.TokenRotatedOn, c.MaxAttempts, c.IsActive,
        c.CreatedOn, c.CreatedBy, c.ModifiedOn, c.ModifiedBy);

    private static ClientConfigOutboundUrlDto ToDto(ClientConfigOutboundUrl u) => new(
        u.Id, u.ClientConfigId, u.EventType, u.Method, u.Path, u.IsActive, u.CreatedOn, u.CreatedBy, u.ModifiedOn, u.ModifiedBy);
}
