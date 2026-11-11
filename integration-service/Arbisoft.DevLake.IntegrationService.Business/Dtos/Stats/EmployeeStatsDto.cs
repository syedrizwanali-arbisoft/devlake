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

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;

/// <summary>
/// One engineer's stats, merged from DevLake. Each source holds the stats of the engineer's Arbisoft email and of every other email they commit
/// with, keyed by email and exactly as DevLake returned them.
/// </summary>
public sealed record EmployeeStatsDto(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("mapped_emails")] IReadOnlyList<string> MappedEmails,
    [property: JsonPropertyName("jira")] IReadOnlyDictionary<string, JsonElement> Jira,
    [property: JsonPropertyName("github")] IReadOnlyDictionary<string, JsonElement> Github,
    [property: JsonPropertyName("ai_usage")] IReadOnlyDictionary<string, JsonElement> AiUsage,
    [property: JsonPropertyName("ai_cost")] IReadOnlyDictionary<string, JsonElement> AiCost);
