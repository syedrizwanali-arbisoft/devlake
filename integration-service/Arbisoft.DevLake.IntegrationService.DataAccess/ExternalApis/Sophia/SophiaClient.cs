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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;

public sealed class SophiaClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SophiaOptions> options,
    TimeProvider timeProvider,
    ILogger<SophiaClient> logger) : ISophiaClient, IDisposable
{
    /// <summary>Token requests are safe to retry.</summary>
    public const string TokenHttpClient = "Sophia.Token";

    /// <summary>Callback POSTs are not retried, so each one is a single attempt the caller records.</summary>
    public const string CallbackHttpClient = "Sophia.Callback";

    private const string System = "Sophia";
    private const int DefaultTokenLifetimeSeconds = 300;

    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async Task<string> GetAccessTokenAsync(SophiaConnection connection, CancellationToken cancellationToken = default)
    {
        switch (connection.Auth)
        {
            case StaticApiToken staticToken:
                return !string.IsNullOrWhiteSpace(staticToken.Token)
                    ? staticToken.Token
                    : throw new ExternalApiAuthenticationException(System, $"Client config {connection.ClientConfigId} has an empty API token.");
            case OAuth2ClientCredentials credentials:
                return await GetOAuthTokenAsync(connection, credentials, cancellationToken);
            default:
                throw new ExternalApiAuthenticationException(System, $"Client config {connection.ClientConfigId} has no supported auth type.");
        }
    }

    public async Task<CallbackResult> SendCallbackAsync(
        SophiaConnection connection, string method, string path, string json, string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var uri = new Uri(ExternalApiCalls.NormalizeBaseUrl(connection.BaseUrl, $"Client config {connection.ClientConfigId} base URL"), path.TrimStart('/'));
        var token = await GetAccessTokenAsync(connection, cancellationToken);
        var result = await SendOnceAsync(uri, method, json, token, correlationId, cancellationToken);

        // The token may have been revoked before it expired. Get a new one and try once more.
        if (result.StatusCode == (int)HttpStatusCode.Unauthorized && connection.Auth is OAuth2ClientCredentials credentials)
        {
            _tokens.TryRemove(CacheKey(connection, credentials), out _);
            token = await GetAccessTokenAsync(connection, cancellationToken);
            result = await SendOnceAsync(uri, method, json, token, correlationId, cancellationToken);
        }

        return result;
    }

    public void Dispose() => _tokenLock.Dispose();

    private async Task<CallbackResult> SendOnceAsync(
        Uri uri, string method, string json, string token, string correlationId, CancellationToken cancellationToken)
    {
        using var request = ExternalApiCalls.Request(new HttpMethod(method.ToUpperInvariant()), uri.AbsoluteUri, token, correlationId);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var http = httpClientFactory.CreateClient(CallbackHttpClient);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new CallbackResult(true, (int)response.StatusCode, body, null);
            }

            logger.LogWarning("Sophia callback {Path} returned {StatusCode} for request {CorrelationId}.",
                uri.AbsolutePath, (int)response.StatusCode, correlationId);
            return new CallbackResult(false, (int)response.StatusCode, body,
                $"Callback returned {(int)response.StatusCode} {response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Sophia callback {Path} failed for request {CorrelationId}.", uri.AbsolutePath, correlationId);
            return new CallbackResult(false, null, string.Empty, $"Callback failed: {ex.Message}");
        }
    }

    private async Task<string> GetOAuthTokenAsync(
        SophiaConnection connection, OAuth2ClientCredentials credentials, CancellationToken cancellationToken)
    {
        var key = CacheKey(connection, credentials);
        if (TryGetCached(key, out var token))
        {
            return token;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(key, out token))
            {
                return token;
            }

            var (accessToken, lifetimeSeconds) = await RequestTokenAsync(connection, credentials, cancellationToken);
            var margin = TimeSpan.FromSeconds(Math.Min(options.Value.TokenRenewalMarginSeconds, lifetimeSeconds / 2));
            _tokens[key] = new CachedToken(accessToken, timeProvider.GetUtcNow() + TimeSpan.FromSeconds(lifetimeSeconds) - margin);
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<(string Token, int LifetimeSeconds)> RequestTokenAsync(
        SophiaConnection connection, OAuth2ClientCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.TokenUrl) || string.IsNullOrWhiteSpace(credentials.ClientId)
            || string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            throw new ExternalApiAuthenticationException(System,
                $"Client config {connection.ClientConfigId} needs a token URL, an outbound client ID and a secret.");
        }

        var baseUrl = ExternalApiCalls.NormalizeBaseUrl(connection.BaseUrl, $"Client config {connection.ClientConfigId} base URL");
        var tokenUri = new Uri(baseUrl, credentials.TokenUrl.StartsWith('/') ? credentials.TokenUrl[1..] : credentials.TokenUrl);

        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (!string.IsNullOrWhiteSpace(credentials.Scope))
        {
            form["scope"] = credentials.Scope;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(
            Uri.EscapeDataString(credentials.ClientId) + ":" + Uri.EscapeDataString(credentials.ClientSecret))));

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(TokenHttpClient).SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new ExternalApiAuthenticationException(System, $"The token request to {tokenUri.AbsolutePath} failed: {ex.Message}", null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalApiAuthenticationException(System,
                    $"The token request to {tokenUri.AbsolutePath} returned {(int)response.StatusCode} {response.StatusCode}.", response.StatusCode);
            }

            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                var accessToken = root.TryGetProperty("access_token", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new ExternalApiAuthenticationException(System, "The token response has no access_token.");
                }

                var lifetime = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) && seconds > 0
                    ? seconds
                    : DefaultTokenLifetimeSeconds;
                return (accessToken, lifetime);
            }
            catch (JsonException ex)
            {
                throw new ExternalApiAuthenticationException(System, "The token response is not JSON.", response.StatusCode, ex);
            }
        }
    }

    private bool TryGetCached(string key, out string token)
    {
        if (_tokens.TryGetValue(key, out var cached) && cached.ExpiresAt > timeProvider.GetUtcNow())
        {
            token = cached.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private static string CacheKey(SophiaConnection connection, OAuth2ClientCredentials credentials) =>
        $"{connection.ClientConfigId}|{credentials.TokenUrl}|{credentials.ClientId}";

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
}
