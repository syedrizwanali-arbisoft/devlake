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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IClientConfigService
{
    /// <summary>Every client with its outbound configuration, or none yet.</summary>
    Task<IReadOnlyList<ClientConfigRowDto>> ListAsync(CancellationToken cancellationToken);

    Task<ClientConfigDetailsDto?> GetAsync(long clientRefId, CancellationToken cancellationToken);

    /// <summary>Creates the client's configuration or updates it. Secrets left blank keep their stored values.</summary>
    Task<AdminResult> SaveAsync(long clientRefId, ClientConfigInput input, string savedBy, CancellationToken cancellationToken);

    Task<OutboundUrlDto?> GetUrlAsync(long urlId, CancellationToken cancellationToken);

    Task<AdminResult<long>> CreateUrlAsync(long clientRefId, OutboundUrlInput input, string createdBy, CancellationToken cancellationToken);

    Task<AdminResult> UpdateUrlAsync(long urlId, OutboundUrlInput input, string modifiedBy, CancellationToken cancellationToken);
}

/// <summary>
/// Where and how the service calls a client back. Secrets and tokens are encrypted before they are stored and are never read back to the portal.
/// URLs are checked by <see cref="OutboundUrl"/>, the same code that builds the calls, so what the portal accepts is what the job can send.
/// </summary>
public sealed class ClientConfigService : IClientConfigService
{
    public const int MinAttempts = 1;
    public const int MaxAttemptsLimit = 10;
    private const int MaxFieldLength = 255;
    private const int MaxUrlLength = 500;

    private static readonly string[] AuthTypes = [ClientConfigAuthType.OAuth2ClientCredentials, ClientConfigAuthType.ApiToken];
    private static readonly string[] EventTypes = [RequestEventTypes.EngineerStats, RequestEventTypes.EngineerStatsBatch];
    private static readonly string[] Methods = ["POST", "PUT", "PATCH"];

    private readonly IClientConfigRepository _configs;
    private readonly IClientRepository _clients;
    private readonly ISecretEncryptor _encryptor;
    private readonly TimeProvider _timeProvider;

    public ClientConfigService(IClientConfigRepository configs, IClientRepository clients, ISecretEncryptor encryptor, TimeProvider timeProvider)
    {
        _configs = configs;
        _clients = clients;
        _encryptor = encryptor;
        _timeProvider = timeProvider;
    }

    /// <summary>The event types a callback URL can be configured for.</summary>
    public static IReadOnlyList<string> KnownEventTypes => EventTypes;

    public static IReadOnlyList<string> KnownMethods => Methods;

    public static IReadOnlyList<string> KnownAuthTypes => AuthTypes;

    public async Task<IReadOnlyList<ClientConfigRowDto>> ListAsync(CancellationToken cancellationToken)
    {
        var clients = await _clients.ListAsync(1, AdminPaging.MaxRows, cancellationToken);
        var configs = (await _configs.ListAsync(1, AdminPaging.MaxRows, cancellationToken)).Items.ToDictionary(c => c.ClientRefId);
        return clients.Items
            .Select(c => new ClientConfigRowDto(c.Id, c.ClientId, c.Name, configs.TryGetValue(c.Id, out var config) ? ToDto(config) : null))
            .ToList();
    }

    public async Task<ClientConfigDetailsDto?> GetAsync(long clientRefId, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(clientRefId, cancellationToken);
        if (client is null)
        {
            return null;
        }

        var config = await _configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        var urls = config is null ? [] : (await _configs.GetOutboundUrlsAsync(config.Id, cancellationToken)).Select(ToDto).ToList();
        return new ClientConfigDetailsDto(client.Id, client.ClientId, client.Name, config is null ? null : ToDto(config), urls);
    }

    public async Task<AdminResult> SaveAsync(long clientRefId, ClientConfigInput input, string savedBy, CancellationToken cancellationToken)
    {
        if (await _clients.GetByIdAsync(clientRefId, cancellationToken) is null)
        {
            return AdminResult.Fail("The client does not exist.");
        }

        var existing = await _configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        var problem = Validate(input);
        if (problem is not null)
        {
            return AdminResult.Fail(problem);
        }

        var now = _timeProvider.GetUtcNow();
        var built = Build(input, existing, now);
        if (built.Error is not null)
        {
            return AdminResult.Fail(built.Error);
        }

        var config = built.Config!;
        if (existing is null)
        {
            await _configs.InsertAsync(Stamp(config, clientRefId, id: 0, createdOn: now, createdBy: savedBy, modifiedOn: null, modifiedBy: null), cancellationToken);
        }
        else
        {
            await _configs.UpdateAsync(Stamp(config, clientRefId, existing.Id, existing.CreatedOn, existing.CreatedBy, now, savedBy), cancellationToken);
        }

        return AdminResult.Ok;
    }

    public async Task<OutboundUrlDto?> GetUrlAsync(long urlId, CancellationToken cancellationToken)
    {
        var url = await _configs.GetOutboundUrlByIdAsync(urlId, cancellationToken);
        return url is null ? null : ToDto(url);
    }

    public async Task<AdminResult<long>> CreateUrlAsync(long clientRefId, OutboundUrlInput input, string createdBy, CancellationToken cancellationToken)
    {
        var config = await _configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        if (config is null)
        {
            return AdminResult.Failure<long>("Save the client's outbound configuration before adding URLs.");
        }

        var problem = await ValidateUrlAsync(config, input, exceptId: null, cancellationToken);
        if (problem is not null)
        {
            return AdminResult.Failure<long>(problem);
        }

        var id = await _configs.InsertOutboundUrlAsync(
            new ClientConfigOutboundUrl
            {
                ClientConfigId = config.Id,
                EventType = input.EventType,
                Method = input.Method,
                Path = input.Path.Trim(),
                IsActive = input.IsActive,
                CreatedOn = _timeProvider.GetUtcNow(),
                CreatedBy = createdBy
            },
            cancellationToken);
        return AdminResult.Success(id);
    }

    public async Task<AdminResult> UpdateUrlAsync(long urlId, OutboundUrlInput input, string modifiedBy, CancellationToken cancellationToken)
    {
        var existing = await _configs.GetOutboundUrlByIdAsync(urlId, cancellationToken);
        if (existing is null)
        {
            return AdminResult.Fail("The URL does not exist.");
        }

        var config = await _configs.GetByIdAsync(existing.ClientConfigId, cancellationToken);
        if (config is null)
        {
            return AdminResult.Fail("The URL's configuration does not exist.");
        }

        var problem = await ValidateUrlAsync(config, input, urlId, cancellationToken);
        if (problem is not null)
        {
            return AdminResult.Fail(problem);
        }

        await _configs.UpdateOutboundUrlAsync(
            new ClientConfigOutboundUrl
            {
                Id = urlId,
                ClientConfigId = existing.ClientConfigId,
                EventType = input.EventType,
                Method = input.Method,
                Path = input.Path.Trim(),
                IsActive = input.IsActive,
                CreatedOn = existing.CreatedOn,
                CreatedBy = existing.CreatedBy,
                ModifiedOn = _timeProvider.GetUtcNow(),
                ModifiedBy = modifiedBy
            },
            cancellationToken);
        return AdminResult.Ok;
    }

    private static string? Validate(ClientConfigInput input)
    {
        if (!AuthTypes.Contains(input.AuthType))
        {
            return "Choose an auth type.";
        }

        var baseUrlProblem = CheckUrl(input.BaseUrl, "base URL");
        if (baseUrlProblem is not null)
        {
            return baseUrlProblem;
        }

        if (input.MaxAttempts is < MinAttempts or > MaxAttemptsLimit)
        {
            return $"Max attempts must be {MinAttempts} to {MaxAttemptsLimit}.";
        }

        if (input.AuthType == ClientConfigAuthType.OAuth2ClientCredentials)
        {
            var tokenUrlProblem = CheckUrl(input.TokenUrl, "token URL");
            if (tokenUrlProblem is not null)
            {
                return tokenUrlProblem;
            }

            if (string.IsNullOrWhiteSpace(input.OutboundClientId) || input.OutboundClientId.Trim().Length > MaxFieldLength)
            {
                return $"The client ID is required and can be at most {MaxFieldLength} characters.";
            }

            if ((input.OutboundScope ?? string.Empty).Trim().Length > MaxFieldLength)
            {
                return $"The scope can be at most {MaxFieldLength} characters.";
            }
        }

        return null;
    }

    private static string? CheckUrl(string? url, string description)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Trim().Length > MaxUrlLength)
        {
            return $"The {description} is required and can be at most {MaxUrlLength} characters.";
        }

        try
        {
            OutboundUrl.Parse(url.Trim(), description);
            return null;
        }
        catch (CallbackNotConfiguredException exception)
        {
            return exception.Message;
        }
    }

    private (ClientConfig? Config, string? Error) Build(ClientConfigInput input, ClientConfig? existing, DateTimeOffset now)
    {
        var sameType = existing is not null && existing.AuthType == input.AuthType;
        var newSecret = string.IsNullOrWhiteSpace(input.OutboundClientSecret) ? null : input.OutboundClientSecret;
        var newToken = string.IsNullOrWhiteSpace(input.ApiToken) ? null : input.ApiToken;
        var rotatedOn = sameType ? existing!.TokenRotatedOn : null;
        string? encryptedSecret = null;
        string? encryptedToken = null;

        if (input.AuthType == ClientConfigAuthType.OAuth2ClientCredentials)
        {
            encryptedSecret = newSecret is not null ? _encryptor.Encrypt(newSecret) : (sameType ? existing!.OutboundClientSecretEncrypted : null);
            if (encryptedSecret is null)
            {
                return (null, "The client secret is required.");
            }

            rotatedOn = newSecret is not null ? now : rotatedOn;
        }
        else
        {
            encryptedToken = newToken is not null ? _encryptor.Encrypt(newToken) : (sameType ? existing!.ApiTokenEncrypted : null);
            if (encryptedToken is null)
            {
                return (null, "The API token is required.");
            }

            rotatedOn = newToken is not null ? now : rotatedOn;
        }

        var oauth = input.AuthType == ClientConfigAuthType.OAuth2ClientCredentials;
        return (
            new ClientConfig
            {
                AuthType = input.AuthType,
                BaseUrl = input.BaseUrl.Trim(),
                TokenUrl = oauth ? input.TokenUrl!.Trim() : null,
                OutboundClientId = oauth ? input.OutboundClientId!.Trim() : null,
                OutboundClientSecretEncrypted = encryptedSecret,
                OutboundScope = oauth && !string.IsNullOrWhiteSpace(input.OutboundScope) ? input.OutboundScope.Trim() : null,
                ApiTokenEncrypted = encryptedToken,
                TokenExpiresOn = oauth ? null : input.TokenExpiresOn,
                TokenRotatedOn = rotatedOn,
                MaxAttempts = input.MaxAttempts,
                IsActive = input.IsActive
            },
            null);
    }

    private static ClientConfig Stamp(
        ClientConfig built, long clientRefId, long id, DateTimeOffset createdOn, string createdBy, DateTimeOffset? modifiedOn, string? modifiedBy)
    {
        return new ClientConfig
        {
            Id = id,
            ClientRefId = clientRefId,
            AuthType = built.AuthType,
            BaseUrl = built.BaseUrl,
            TokenUrl = built.TokenUrl,
            OutboundClientId = built.OutboundClientId,
            OutboundClientSecretEncrypted = built.OutboundClientSecretEncrypted,
            OutboundScope = built.OutboundScope,
            ApiTokenEncrypted = built.ApiTokenEncrypted,
            TokenExpiresOn = built.TokenExpiresOn,
            TokenRotatedOn = built.TokenRotatedOn,
            MaxAttempts = built.MaxAttempts,
            IsActive = built.IsActive,
            CreatedOn = createdOn,
            CreatedBy = createdBy,
            ModifiedOn = modifiedOn,
            ModifiedBy = modifiedBy
        };
    }

    private async Task<string?> ValidateUrlAsync(ClientConfig config, OutboundUrlInput input, long? exceptId, CancellationToken cancellationToken)
    {
        if (!EventTypes.Contains(input.EventType))
        {
            return "Choose an event type.";
        }

        if (!Methods.Contains(input.Method))
        {
            return "Choose a method.";
        }

        if (string.IsNullOrWhiteSpace(input.Path) || input.Path.Trim().Length > MaxUrlLength)
        {
            return $"The path is required and can be at most {MaxUrlLength} characters.";
        }

        try
        {
            OutboundUrl.Combine(config.BaseUrl, input.Path.Trim());
        }
        catch (CallbackNotConfiguredException exception)
        {
            return exception.Message;
        }

        if (input.IsActive)
        {
            var urls = await _configs.GetOutboundUrlsAsync(config.Id, cancellationToken);
            if (urls.Any(u => u.IsActive && u.EventType == input.EventType && u.Id != exceptId))
            {
                return $"Another URL for '{input.EventType}' is already active. Deactivate it first, because only one can be active per event type.";
            }
        }

        return null;
    }

    private static ClientConfigDto ToDto(ClientConfig c) => new(
        c.Id, c.AuthType, c.BaseUrl, c.TokenUrl, c.OutboundClientId, c.OutboundScope,
        !string.IsNullOrEmpty(c.OutboundClientSecretEncrypted), !string.IsNullOrEmpty(c.ApiTokenEncrypted),
        c.TokenExpiresOn, c.TokenRotatedOn, c.MaxAttempts, c.IsActive, c.CreatedOn, c.CreatedBy, c.ModifiedOn, c.ModifiedBy);

    private static OutboundUrlDto ToDto(ClientConfigOutboundUrl u) =>
        new(u.Id, u.ClientConfigId, u.EventType, u.Method, u.Path, u.IsActive, u.CreatedOn, u.CreatedBy, u.ModifiedOn, u.ModifiedBy);
}
