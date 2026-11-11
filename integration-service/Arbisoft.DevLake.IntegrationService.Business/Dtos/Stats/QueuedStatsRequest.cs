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

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;

/// <summary>
/// The JSON body queued in RabbitMQ and kept in RequestLogJsons. The processing job reads it back, so changes must be additive.
/// A request covers either a <c>time_frame</c> such as <c>24h</c>, or a range from <c>from</c> to <c>to</c> (UTC, second precision); the
/// fields that do not apply are left out.
/// </summary>
public sealed record QueuedStatsRequest(
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("emails")] IReadOnlyList<string> Emails,
    [property: JsonPropertyName("requested_on")] DateTimeOffset RequestedOn,
    [property: JsonPropertyName("time_frame"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimeFrame = null,
    [property: JsonPropertyName("from"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From = null,
    [property: JsonPropertyName("to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To = null);
