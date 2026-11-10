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
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Projects;

public interface IErpIntegrationService
{
    /// <summary>Checks the Workstream token, then reads every projects page. Any failure returns an error, never a partial list.</summary>
    Task<ProjectsResult> GetProjectsAsync(CancellationToken cancellationToken = default);
}

public sealed class ErpIntegrationService(IWorkstreamClient workstream, ILogger<ErpIntegrationService> logger) : IErpIntegrationService
{
    public async Task<ProjectsResult> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await workstream.IsTokenValidAsync(cancellationToken))
            {
                return new ProjectsResult(ProjectsOutcome.TokenRejected, null, "Workstream rejected the Integration Service's API token.");
            }

            var items = await workstream.GetAllProjectsAsync(cancellationToken);
            var projects = items.Select(ToDto).ToList();
            return new ProjectsResult(ProjectsOutcome.Success, new ProjectsResponse(projects.Count, projects), null);
        }
        catch (ExternalApiAuthenticationException ex)
        {
            logger.LogError(ex, "Loading Workstream projects failed: the token was rejected or isn't set.");
            return new ProjectsResult(ProjectsOutcome.TokenRejected, null, ex.Message);
        }
        catch (ExternalApiException ex)
        {
            logger.LogError(ex, "Loading Workstream projects failed.");
            return new ProjectsResult(ProjectsOutcome.Unavailable, null, ex.Message);
        }
    }

    /// <summary>
    /// Maps the fields a projects API most likely uses. The team lead may be an object ("team_lead", "teamLead" or
    /// "lead" with "name" or "full_name" and "email") or flat fields ("team_lead_name", "team_lead_email").
    /// </summary>
    public static ProjectDto ToDto(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return new ProjectDto(null, null, null, item);
        }

        var lead = FirstObject(item, "team_lead", "teamLead", "lead");
        var leadName = lead is { } l ? Text(l, "name", "full_name", "fullName") : Text(item, "team_lead_name", "teamLeadName");
        var leadEmail = lead is { } e ? Text(e, "email") : Text(item, "team_lead_email", "teamLeadEmail");
        var teamLead = leadName is null && leadEmail is null ? null : new TeamLeadDto(leadName, leadEmail?.ToLowerInvariant());

        return new ProjectDto(Text(item, "id", "project_id", "projectId"), Text(item, "name", "title", "project_name"), teamLead, item);
    }

    private static JsonElement? FirstObject(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString()):
                    return value.GetString()!.Trim();
                case JsonValueKind.Number:
                    return value.GetRawText();
            }
        }

        return null;
    }
}
