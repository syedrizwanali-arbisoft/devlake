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

using System.Text.Json.Serialization;

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;

/// <summary>
/// A Workstream project with its team lead, as this service returns it to DevLake. It is an explicit allow-list: only these fields leave the service,
/// whatever else Workstream sends.
/// </summary>
public sealed record ProjectDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("team_lead")] TeamLeadDto? TeamLead);

/// <summary>A project's team lead. Either part may be missing when Workstream does not have it.</summary>
public sealed record TeamLeadDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email);
