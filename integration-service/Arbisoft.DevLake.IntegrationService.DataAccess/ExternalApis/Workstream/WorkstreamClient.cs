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

using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;

public sealed class WorkstreamClient(HttpClient http, IOptions<WorkstreamOptions> options, ILogger<WorkstreamClient> logger)
    : IWorkstreamClient
{
    private const string System = "Workstream";

    public async Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        using var request = ExternalApiCalls.Request(HttpMethod.Get, "is_valid", Token(), null);
        using var response = await ExternalApiCalls.SendAsync(http, request, System, null, logger, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning("Workstream rejected the API token ({StatusCode}).", (int)response.StatusCode);
            return false;
        }

        await ExternalApiCalls.EnsureSuccessAsync(response, System, null, logger, cancellationToken);
        var body = await ExternalApiCalls.ReadJsonAsync(response, System, cancellationToken);
        return body.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ExternalApiException(System, "/is_valid must return true or false."),
        };
    }

    public async Task<IReadOnlyList<JsonElement>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var projects = new List<JsonElement>();
        for (var page = 1; page <= settings.MaxPages; page++)
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"projects?page={page}&pageSize={settings.PageSize}");
            using var request = ExternalApiCalls.Request(HttpMethod.Get, path, Token(), null);
            using var response = await ExternalApiCalls.SendAsync(http, request, System, null, logger, cancellationToken);
            await ExternalApiCalls.EnsureSuccessAsync(response, System, null, logger, cancellationToken);
            var body = await ExternalApiCalls.ReadJsonAsync(response, System, cancellationToken);

            var (items, isLastPage) = ReadPage(body, settings.PageSize, page);
            projects.AddRange(items);
            if (isLastPage)
            {
                return projects;
            }
        }

        throw new ExternalApiException(System, $"/projects had more than {settings.MaxPages} pages.");
    }

    private static (List<JsonElement> Items, bool IsLastPage) ReadPage(JsonElement body, int pageSize, int page)
    {
        if (body.ValueKind == JsonValueKind.Array)
        {
            var items = body.EnumerateArray().Select(item => item.Clone()).ToList();
            return (items, items.Count < pageSize);
        }

        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            var items = results.EnumerateArray().Select(item => item.Clone()).ToList();
            var hasNext = body.TryGetProperty("next", out var next) && next.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            return (items, !hasNext || items.Count == 0);
        }

        throw new ExternalApiException(System, $"/projects page {page} is neither an array nor an object with a results array.");
    }

    private string Token()
    {
        if (http.BaseAddress is null)
        {
            throw new ExternalApiException(System, "Workstream:BaseUrl is not set to an absolute URL.");
        }

        return !string.IsNullOrWhiteSpace(options.Value.ApiToken)
            ? options.Value.ApiToken
            : throw new ExternalApiAuthenticationException(System, "Workstream:ApiToken is not set. Set the Workstream__ApiToken environment variable.");
    }
}
