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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Projects;

/// <summary>
/// Turns one project as Workstream returned it into a <see cref="ProjectDto"/>. Workstream's API is not documented, so each field is looked up under
/// the names it is most likely to have, in a fixed order. A project that has no id or no name cannot be used and is reported, never guessed at (MAP-9).
/// The function is pure (MAP-8).
/// </summary>
public static class ProjectMapper
{
    private static readonly string[] IdNames = ["id", "project_id", "projectId", "uuid"];
    private static readonly string[] NameNames = ["name", "title", "project_name", "projectName"];
    private static readonly string[] TeamLeadObjectNames = ["team_lead", "teamLead", "lead", "manager", "project_manager", "projectManager"];
    private static readonly string[] LeadNameNames = ["name", "full_name", "fullName", "display_name", "displayName"];
    private static readonly string[] LeadEmailNames = ["email", "email_address", "emailAddress"];
    private static readonly string[] FlatLeadNameNames = ["team_lead_name", "teamLeadName"];
    private static readonly string[] FlatLeadEmailNames = ["team_lead_email", "teamLeadEmail"];

    public static bool TryMap(JsonElement item, out ProjectDto? project, out string? problem)
    {
        project = null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            problem = "a project is not a JSON object";
            return false;
        }

        var id = ReadText(item, IdNames);
        if (id is null)
        {
            problem = "a project has no id";
            return false;
        }

        var name = ReadText(item, NameNames);
        if (name is null)
        {
            problem = "a project has no name";
            return false;
        }

        project = new ProjectDto(id, name, ReadTeamLead(item));
        problem = null;
        return true;
    }

    private static TeamLeadDto? ReadTeamLead(JsonElement item)
    {
        foreach (var property in TeamLeadObjectNames)
        {
            if (!item.TryGetProperty(property, out var lead))
            {
                continue;
            }

            if (lead.ValueKind == JsonValueKind.Object)
            {
                return Lead(ReadText(lead, LeadNameNames), ReadText(lead, LeadEmailNames));
            }

            if (lead.ValueKind == JsonValueKind.String && lead.GetString()?.Trim() is { Length: > 0 } text)
            {
                return text.Contains('@', StringComparison.Ordinal) ? Lead(null, text) : Lead(text, null);
            }
        }

        return Lead(ReadText(item, FlatLeadNameNames), ReadText(item, FlatLeadEmailNames));
    }

    private static TeamLeadDto? Lead(string? name, string? email)
    {
        var canonicalEmail = email is not null && email.Contains('@', StringComparison.Ordinal) ? EmailNormalizer.Canonical(email) : null;
        return name is null && canonicalEmail is null ? null : new TeamLeadDto(name, canonicalEmail);
    }

    /// <summary>The first of the given properties that holds a non-blank string or a number, as trimmed text.</summary>
    private static string? ReadText(JsonElement element, string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    /// <summary>Formats a page number for an error message.</summary>
    internal static string PageText(int page) => page.ToString(CultureInfo.InvariantCulture);
}
