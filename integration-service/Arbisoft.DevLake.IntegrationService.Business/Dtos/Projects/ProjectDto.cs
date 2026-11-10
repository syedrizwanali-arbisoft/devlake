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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;

public sealed record TeamLeadDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email);

/// <summary>
/// A Workstream project with its team lead. Workstream's fields aren't documented yet, so the common ones are mapped
/// and the original item is kept in <see cref="Source"/> for the caller.
/// </summary>
public sealed record ProjectDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("team_lead")] TeamLeadDto? TeamLead,
    [property: JsonPropertyName("source")] JsonElement Source);

public sealed record ProjectsResponse(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("projects")] IReadOnlyList<ProjectDto> Projects);

public enum ProjectsOutcome
{
    Success,
    TokenRejected,
    Unavailable,
}

public sealed record ProjectsResult(ProjectsOutcome Outcome, ProjectsResponse? Response, string? Error);
