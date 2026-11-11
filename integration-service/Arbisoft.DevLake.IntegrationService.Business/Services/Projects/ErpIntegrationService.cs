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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Projects;

public sealed class ErpIntegrationService : IErpIntegrationService
{
    private const string SystemName = "Workstream";

    /// <summary>A safety stop for a Workstream that ignores paging and keeps answering with full pages. It is far above any real project count.</summary>
    public const int MaxPages = 10_000;

    private readonly IWorkstreamClient _workstream;
    private readonly WorkstreamOptions _options;
    private readonly ILogger<ErpIntegrationService> _logger;

    public ErpIntegrationService(IWorkstreamClient workstream, IOptions<WorkstreamOptions> options, ILogger<ErpIntegrationService> logger)
    {
        _workstream = workstream;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProjectsFetchResult> FetchProjectsAsync(string correlationId, CancellationToken cancellationToken)
    {
        if (!await _workstream.IsTokenValidAsync(correlationId, cancellationToken))
        {
            _logger.LogError("Workstream rejected this service's API token (request {CorrelationId})", correlationId);
            return new ProjectsFetchResult(ProjectsFetchOutcome.TokenInvalid, []);
        }

        var projects = await ReadAllPagesAsync(correlationId, cancellationToken);
        _logger.LogInformation("Fetched {Count} projects from Workstream for request {CorrelationId}", projects.Count, correlationId);
        return new ProjectsFetchResult(ProjectsFetchOutcome.Fetched, projects);
    }

    /// <summary>
    /// Reads page after page until a short page, a page with nothing new on it, or the page limit. Projects seen on an earlier page are kept once, so a
    /// project that moves while paging is not listed twice. A failure on any page propagates, so the caller never gets part of the list.
    /// </summary>
    private async Task<List<ProjectDto>> ReadAllPagesAsync(string correlationId, CancellationToken cancellationToken)
    {
        var projects = new List<ProjectDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; page <= MaxPages; page++)
        {
            var current = await _workstream.GetProjectsPageAsync(page, _options.PageSize, correlationId, cancellationToken);
            var added = 0;
            foreach (var item in current.Items)
            {
                var project = Map(item, page);
                if (seen.Add(project.Id))
                {
                    projects.Add(project);
                    added++;
                }
            }

            if (current.IsLast || added == 0)
            {
                return projects;
            }
        }

        throw new ExternalApiResponseException(SystemName, "Workstream returned more pages of projects than expected.");
    }

    private static ProjectDto Map(System.Text.Json.JsonElement item, int page)
    {
        return ProjectMapper.TryMap(item, out var project, out var problem)
            ? project!
            : throw new ExternalApiResponseException(SystemName, $"Workstream's project list could not be read on page {ProjectMapper.PageText(page)}: {problem}.");
    }
}
