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
using System.Net.Http.Headers;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;

public sealed class WorkstreamClient : IWorkstreamClient
{
    private const string SystemName = "Workstream";
    private const string TokenValidityPath = "is_valid";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkstreamOptions _options;
    private readonly ILogger<WorkstreamClient> _logger;

    public WorkstreamClient(IHttpClientFactory httpClientFactory, IOptions<WorkstreamOptions> options, ILogger<WorkstreamClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> IsTokenValidAsync(string correlationId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(TokenValidityPath, correlationId, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return false;
        }

        await ExternalApiCall.EnsureSuccessAsync(response, SystemName, correlationId, _logger, cancellationToken);
        var body = await ExternalApiCall.ReadJsonAsync(response, SystemName, cancellationToken);
        return body.ValueKind == JsonValueKind.True;
    }

    public async Task<WorkstreamProjectsPage> GetProjectsPageAsync(int page, int pageSize, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var path = string.Create(CultureInfo.InvariantCulture, $"projects?page={page}&pageSize={pageSize}");
        using var response = await SendAsync(path, correlationId, cancellationToken);
        await ExternalApiCall.EnsureSuccessAsync(response, SystemName, correlationId, _logger, cancellationToken);

        var body = await ExternalApiCall.ReadJsonAsync(response, SystemName, cancellationToken);
        return WorkstreamProjectsPage.FromJson(body, page, pageSize)
            ?? throw new ExternalApiResponseException(SystemName, "Workstream's project list did not contain a list of projects.");
    }

    private async Task<HttpResponseMessage> SendAsync(string relativeUrl, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        request.Headers.Add(ExternalApiHeaders.CorrelationId, correlationId);
        return await ExternalApiCall.SendAsync(_httpClientFactory.CreateClient(ExternalHttpClients.Workstream), request, SystemName, correlationId, _logger, cancellationToken);
    }
}
