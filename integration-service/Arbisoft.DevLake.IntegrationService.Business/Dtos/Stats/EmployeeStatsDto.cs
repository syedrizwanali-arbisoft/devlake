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

/// <summary>One mapped email's response from a DevLake stats endpoint.</summary>
public sealed record EmailStats(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("stats")] JsonElement Stats);

/// <summary>
/// An engineer's stats from every email they use, grouped by category. DevLake's response shapes are defined in
/// DEVLAKE-418, so each email's response is kept as is rather than summed.
/// </summary>
public sealed record EmployeeStatsDto(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("mapped_emails")] IReadOnlyList<string> MappedEmails,
    [property: JsonPropertyName("jira")] IReadOnlyList<EmailStats> Jira,
    [property: JsonPropertyName("github")] IReadOnlyList<EmailStats> Github,
    [property: JsonPropertyName("ai_usage")] IReadOnlyList<EmailStats> AiUsage,
    [property: JsonPropertyName("ai_cost")] IReadOnlyList<EmailStats> AiCost);

/// <summary>The body posted to the client's callback, for single and batch requests alike.</summary>
public sealed record StatsCallbackPayload(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("time_frame")] string TimeFrame,
    [property: JsonPropertyName("engineers")] IReadOnlyList<EmployeeStatsDto> Engineers);
