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

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;

/// <summary>The filters of the request log list. Dates are UTC days; <see cref="To"/> is included.</summary>
public sealed record RequestLogQuery(long? ClientRefId = null, string? Status = null, DateOnly? From = null, DateOnly? To = null);

public sealed record RequestLogDto(long Id, string ClientId, string ClientName, string CorrelationId, string EventType, string Status, DateTimeOffset CreatedOn);

public sealed record DeliveryAttemptDto(DateTimeOffset AttemptOn, string AttemptStatus, string AttemptBy, string? FailureError, string ResponseJson);

/// <summary>One request with its stored JSON body and every delivery attempt. The body holds engineer emails, so only the admin portal shows it.</summary>
public sealed record RequestLogDetailsDto(
    long Id,
    string ClientId,
    string ClientName,
    string CorrelationId,
    string EventType,
    string Status,
    string Url,
    DateTimeOffset CreatedOn,
    string? Json,
    IReadOnlyList<DeliveryAttemptDto> Attempts);
