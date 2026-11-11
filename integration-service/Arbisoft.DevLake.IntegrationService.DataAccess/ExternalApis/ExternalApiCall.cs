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
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

/// <summary>
/// Sends a request to an upstream system and turns every way it can go wrong into one of the <see cref="ExternalApiException"/> types, so callers
/// handle three outcomes (credentials, unavailable, rejected) however many clients there are. Failures are logged with the correlation ID and
/// never with the request URL or body, which can hold an email.
/// </summary>
internal static class ExternalApiCall
{
    private const int MaxLoggedBodyLength = 200;

    /// <summary>Sends the request and returns the response. Transport failures and an open circuit become <see cref="ExternalApiUnavailableException"/>.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpRequestMessage request, string system, string correlationId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (BrokenCircuitException exception)
        {
            logger.LogWarning("{System} call stopped by an open circuit breaker for request {CorrelationId}", system, correlationId);
            throw new ExternalApiUnavailableException(system, $"{system} is failing, so calls to it are paused.", null, exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or TimeoutRejectedException or TaskCanceledException)
        {
            logger.LogWarning("{System} call failed for request {CorrelationId}: {Failure}", system, correlationId, exception.GetType().Name);
            throw new ExternalApiUnavailableException(system, $"{system} could not be reached or timed out.", null, exception);
        }
    }

    /// <summary>Throws the right exception for an unsuccessful response, or returns when the status is a success.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string system, string correlationId, ILogger logger, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        logger.LogWarning("{System} answered {Status} for request {CorrelationId}: {Body}", system, status, correlationId, await ReadBodyForLogAsync(response, cancellationToken));

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ExternalAuthenticationException(system, $"{system} rejected our credentials ({status}).", status),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError => new ExternalApiUnavailableException(system, $"{system} was unavailable ({status}).", status),
            _ => new ExternalApiRejectedException(system, $"{system} rejected the request ({status}).", status)
        };
    }

    /// <summary>Reads a successful response body as JSON. The returned element stays valid after the response is disposed.</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string system, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ExternalApiResponseException(system, $"{system} answered with something that is not JSON.", exception);
        }
    }

    private static async Task<string> ReadBodyForLogAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength] + "...";
    }
}
