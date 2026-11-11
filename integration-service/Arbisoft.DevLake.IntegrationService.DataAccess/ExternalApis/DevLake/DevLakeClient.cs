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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

public sealed class DevLakeClient : IDevLakeClient
{
    private const string SystemName = "DevLake";
    private const string TokenValidityPath = "is_valid";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DevLakeOptions _options;
    private readonly DevLakeConcurrencyGate _gate;
    private readonly ILogger<DevLakeClient> _logger;

    public DevLakeClient(IHttpClientFactory httpClientFactory, IOptions<DevLakeOptions> options, DevLakeConcurrencyGate gate, ILogger<DevLakeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _gate = gate;
        _logger = logger;
    }

    public async Task<bool> IsTokenValidAsync(string correlationId, CancellationToken cancellationToken)
    {
        return await _gate.RunAsync(
            async () =>
            {
                using var response = await SendAsync(TokenValidityPath, correlationId, cancellationToken);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return false;
                }

                await ExternalApiCall.EnsureSuccessAsync(response, SystemName, correlationId, _logger, cancellationToken);
                var body = await ExternalApiCall.ReadJsonAsync(response, SystemName, cancellationToken);
                return body.ValueKind == JsonValueKind.True;
            },
            cancellationToken);
    }

    public async Task<JsonElement> GetStatsAsync(DevLakeStatsSource source, string email, StatsQuery query, string correlationId, CancellationToken cancellationToken)
    {
        var path = $"stats/{Uri.EscapeDataString(email)}/{PathOf(source)}?{query.ToQueryString()}";
        return await _gate.RunAsync(
            async () =>
            {
                using var response = await SendAsync(path, correlationId, cancellationToken);
                await ExternalApiCall.EnsureSuccessAsync(response, SystemName, correlationId, _logger, cancellationToken);
                return await ExternalApiCall.ReadJsonAsync(response, SystemName, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>The path segment of each stats endpoint, as DevLake names it.</summary>
    internal static string PathOf(DevLakeStatsSource source) => source switch
    {
        DevLakeStatsSource.Jira => "jira",
        DevLakeStatsSource.Github => "github",
        DevLakeStatsSource.AiUsage => "ai_usage",
        DevLakeStatsSource.AiCost => "ai_cost",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown DevLake stats source.")
    };

    private async Task<HttpResponseMessage> SendAsync(string relativeUrl, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        request.Headers.Add(ExternalApiHeaders.CorrelationId, correlationId);
        return await ExternalApiCall.SendAsync(_httpClientFactory.CreateClient(ExternalHttpClients.DevLake), request, SystemName, correlationId, _logger, cancellationToken);
    }
}
