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
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

/// <summary>Sending, error handling and logging shared by the external API clients.</summary>
internal static class ExternalApiCalls
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    public static HttpRequestMessage Request(HttpMethod method, string uri, string? bearerToken, string? correlationId)
    {
        var request = new HttpRequestMessage(method, uri);
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (!string.IsNullOrEmpty(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        }

        return request;
    }

    /// <summary>Sends the request. Transport failures and timeouts become an <see cref="ExternalApiException"/>.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpRequestMessage request, string system, string? correlationId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{System} call {Method} {Path} failed for request {CorrelationId}.",
                system, request.Method, request.RequestUri?.AbsolutePath, correlationId);
            throw new ExternalApiException(system, $"{request.Method} {request.RequestUri?.AbsolutePath} failed: {ex.Message}", null, ex);
        }
    }

    /// <summary>Throws for a non-success status. 401 and 403 become an <see cref="ExternalApiAuthenticationException"/>.</summary>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string system, string? correlationId, ILogger logger, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await ReadSnippetAsync(response, cancellationToken);
        var path = response.RequestMessage?.RequestUri?.AbsolutePath;
        logger.LogWarning("{System} call {Path} returned {StatusCode} for request {CorrelationId}: {Body}",
            system, path, (int)response.StatusCode, correlationId, body);

        var message = $"{path} returned {(int)response.StatusCode} {response.StatusCode}.";
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ExternalApiAuthenticationException(system, message + " The API token was rejected.", response.StatusCode);
        }

        throw new ExternalApiException(system, message, response.StatusCode);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string system, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ExternalApiException(system,
                $"{response.RequestMessage?.RequestUri?.AbsolutePath} returned a body that is not JSON.", response.StatusCode, ex);
        }
    }

    public static Uri NormalizeBaseUrl(string? baseUrl, string setting)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{setting} must be an absolute URL.");
        }

        // Without a trailing slash, relative paths would replace the last segment of the base URL.
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Length <= 500 ? body : body[..500] + "...";
    }
}
