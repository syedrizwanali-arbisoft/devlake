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

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;

public sealed class SophiaClient : ISophiaClient
{
    private const string SystemName = "Sophia";
    private const int MaxStoredResponseLength = 8192;
    private const int DefaultTokenLifetimeSeconds = 300;

    private static readonly string[] AllowedMethods = ["POST", "PUT", "PATCH"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClientConfigRepository _configs;
    private readonly ISecretDecryptor _decryptor;
    private readonly SophiaTokenCache _tokenCache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SophiaClient> _logger;

    public SophiaClient(
        IHttpClientFactory httpClientFactory,
        IClientConfigRepository configs,
        ISecretDecryptor decryptor,
        SophiaTokenCache tokenCache,
        TimeProvider timeProvider,
        ILogger<SophiaClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configs = configs;
        _decryptor = decryptor;
        _tokenCache = tokenCache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SophiaDeliveryResult> PostCallbackAsync(long clientRefId, string eventType, string payloadJson, string correlationId, CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(clientRefId, eventType, cancellationToken);

        var result = await SendOnceAsync(target, payloadJson, correlationId, cancellationToken);
        if (result.StatusCode == (int)HttpStatusCode.Unauthorized && target.Config.AuthType == ClientConfigAuthType.OAuth2ClientCredentials)
        {
            // The cached token may have been revoked or expired early: replace it once and repeat the call.
            _logger.LogInformation("Sophia rejected the cached access token for request {CorrelationId}; fetching a new one", correlationId);
            _tokenCache.Invalidate(target.TokenKey);
            result = await SendOnceAsync(target, payloadJson, correlationId, cancellationToken);
        }

        if (result.StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
        {
            throw new ExternalAuthenticationException(SystemName, $"Sophia rejected our access token ({result.StatusCode}).", result.StatusCode);
        }

        return result;
    }

    public async Task EnsureAccessTokenAsync(long clientRefId, string correlationId, CancellationToken cancellationToken)
    {
        var config = await ActiveConfigAsync(clientRefId, cancellationToken);
        await GetAccessTokenAsync(config, TokenKeyOf(config), correlationId, cancellationToken);
    }

    private async Task<ClientConfig> ActiveConfigAsync(long clientRefId, CancellationToken cancellationToken)
    {
        var config = await _configs.GetByClientRefIdAsync(clientRefId, cancellationToken);
        return config is { IsActive: true } ? config : throw new CallbackNotConfiguredException("The client has no active outbound configuration.");
    }

    private static string TokenKeyOf(ClientConfig config) => $"{config.Id}:{(config.ModifiedOn ?? config.CreatedOn).UtcTicks}";

    private async Task<Target> ResolveTargetAsync(long clientRefId, string eventType, CancellationToken cancellationToken)
    {
        var config = await ActiveConfigAsync(clientRefId, cancellationToken);

        var url = await _configs.GetActiveOutboundUrlAsync(config.Id, eventType, cancellationToken)
            ?? throw new CallbackNotConfiguredException("The client has no active callback URL for this event type.");

        if (!AllowedMethods.Contains(url.Method, StringComparer.OrdinalIgnoreCase))
        {
            throw new CallbackNotConfiguredException($"The callback method '{url.Method}' is not supported.");
        }

        return new Target(config, OutboundUrl.Combine(config.BaseUrl, url.Path), new HttpMethod(url.Method.ToUpperInvariant()), TokenKeyOf(config));
    }

    private async Task<SophiaDeliveryResult> SendOnceAsync(Target target, string payloadJson, string correlationId, CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(target.Config, target.TokenKey, correlationId, cancellationToken);

        using var request = new HttpRequestMessage(target.Method, target.Endpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(ExternalApiHeaders.CorrelationId, correlationId);

        using var response = await ExternalApiCall.SendAsync(
            _httpClientFactory.CreateClient(ExternalHttpClients.SophiaCallback), request, SystemName, correlationId, _logger, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var stored = body.Length <= MaxStoredResponseLength ? body : body[..MaxStoredResponseLength];

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Sophia's callback answered {Status} for request {CorrelationId}", (int)response.StatusCode, correlationId);
        }

        return new SophiaDeliveryResult(response.IsSuccessStatusCode, (int)response.StatusCode, stored);
    }

    private async Task<string> GetAccessTokenAsync(ClientConfig config, string tokenKey, string correlationId, CancellationToken cancellationToken)
    {
        if (config.AuthType == ClientConfigAuthType.ApiToken)
        {
            return StaticApiToken(config);
        }

        if (config.AuthType != ClientConfigAuthType.OAuth2ClientCredentials
            || string.IsNullOrEmpty(config.TokenUrl) || string.IsNullOrEmpty(config.OutboundClientId) || string.IsNullOrEmpty(config.OutboundClientSecretEncrypted))
        {
            throw new CallbackNotConfiguredException("The outbound configuration is missing its token URL, client ID or client secret.");
        }

        return await _tokenCache.GetOrFetchAsync(tokenKey, token => FetchTokenAsync(config, correlationId, token), cancellationToken);
    }

    private string StaticApiToken(ClientConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiTokenEncrypted))
        {
            throw new CallbackNotConfiguredException("The outbound configuration has no API token.");
        }

        if (config.TokenExpiresOn is { } expiresOn && expiresOn <= _timeProvider.GetUtcNow())
        {
            throw new ExternalAuthenticationException(SystemName, "The stored API token for Sophia has expired. Replace it in the admin portal.");
        }

        return Decrypt(config.ApiTokenEncrypted);
    }

    /// <summary>A stored secret that cannot be decrypted, for example after the master key changed, is a credentials problem to fix, not a crash.</summary>
    private string Decrypt(string protectedValue)
    {
        try
        {
            return _decryptor.Decrypt(protectedValue);
        }
        catch (CryptographicException exception)
        {
            throw new ExternalAuthenticationException(SystemName, "A stored Sophia secret cannot be decrypted. Check the encryption key, then re-enter the secret in the admin portal.", null, exception);
        }
    }

    /// <summary>Asks Sophia's token endpoint for an access token with OAuth2 client credentials. Any refusal is reported as an authentication failure.</summary>
    private async Task<FetchedToken> FetchTokenAsync(ClientConfig config, string correlationId, CancellationToken cancellationToken)
    {
        var tokenUri = OutboundUrl.Parse(config.TokenUrl!, "token URL");
        var secret = Decrypt(config.OutboundClientSecretEncrypted!);
        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        if (!string.IsNullOrEmpty(config.OutboundScope))
        {
            form.Add(new("scope", config.OutboundScope));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.OutboundClientId}:{secret}")));
        request.Headers.Add(ExternalApiHeaders.CorrelationId, correlationId);

        using var response = await ExternalApiCall.SendAsync(
            _httpClientFactory.CreateClient(ExternalHttpClients.SophiaToken), request, SystemName, correlationId, _logger, cancellationToken);
        try
        {
            await ExternalApiCall.EnsureSuccessAsync(response, SystemName, correlationId, _logger, cancellationToken);
        }
        catch (ExternalApiRejectedException exception)
        {
            throw new ExternalAuthenticationException(SystemName, $"Sophia refused to issue an access token ({exception.StatusCode}). Check the client ID and secret.", exception.StatusCode, exception);
        }

        return ReadToken(await ExternalApiCall.ReadJsonAsync(response, SystemName, cancellationToken));
    }

    private static FetchedToken ReadToken(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(token.GetString()))
        {
            throw new ExternalAuthenticationException(SystemName, "Sophia's token response had no access_token.");
        }

        var seconds = body.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var parsed) && parsed > 0 ? parsed : DefaultTokenLifetimeSeconds;
        return new FetchedToken(token.GetString()!, TimeSpan.FromSeconds(seconds));
    }

    private sealed record Target(ClientConfig Config, Uri Endpoint, HttpMethod Method, string TokenKey);
}
