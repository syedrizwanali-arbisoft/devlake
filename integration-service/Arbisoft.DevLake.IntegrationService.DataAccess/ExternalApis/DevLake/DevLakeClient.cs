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
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

public sealed class DevLakeClient(HttpClient http, IOptions<DevLakeOptions> options, ILogger<DevLakeClient> logger) : IDevLakeClient
{
    private const string System = "DevLake";

    public async Task<bool> IsTokenValidAsync(string? correlationId = null, CancellationToken cancellationToken = default)
    {
        using var request = ExternalApiCalls.Request(HttpMethod.Get, "is_valid", Token(), correlationId);
        using var response = await ExternalApiCalls.SendAsync(http, request, System, correlationId, logger, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning("DevLake rejected the API token ({StatusCode}).", (int)response.StatusCode);
            return false;
        }

        await ExternalApiCalls.EnsureSuccessAsync(response, System, correlationId, logger, cancellationToken);
        var body = await ExternalApiCalls.ReadJsonAsync(response, System, cancellationToken);
        return body.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ExternalApiException(System, "/is_valid must return true or false."),
        };
    }

    public async Task<JsonElement> GetStatsAsync(
        string email, DevLakeStatsCategory category, string timeFrame, string correlationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeFrame);

        var path = $"stats/{Uri.EscapeDataString(email)}/{Segment(category)}?tf={Uri.EscapeDataString(timeFrame)}";
        using var request = ExternalApiCalls.Request(HttpMethod.Get, path, Token(), correlationId);
        using var response = await ExternalApiCalls.SendAsync(http, request, System, correlationId, logger, cancellationToken);
        await ExternalApiCalls.EnsureSuccessAsync(response, System, correlationId, logger, cancellationToken);
        return await ExternalApiCalls.ReadJsonAsync(response, System, cancellationToken);
    }

    private static string Segment(DevLakeStatsCategory category) => category switch
    {
        DevLakeStatsCategory.Jira => "jira",
        DevLakeStatsCategory.Github => "github",
        DevLakeStatsCategory.AiUsage => "ai_usage",
        DevLakeStatsCategory.AiCost => "ai_cost",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    private string Token() => !string.IsNullOrWhiteSpace(options.Value.ApiToken)
        ? options.Value.ApiToken
        : throw new ExternalApiAuthenticationException(System, "DevLake:ApiToken is not set. Set the DevLake__ApiToken environment variable.");
}
