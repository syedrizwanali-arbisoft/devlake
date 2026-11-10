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

public sealed record RequestLogDto(
    long Id,
    string CorrelationId,
    long ClientRefId,
    string ClientId,
    string ClientName,
    string Url,
    string Status,
    string EventType,
    DateTime CreatedOn);

public sealed record RequestAttemptDto(
    long Id,
    string AttemptStatus,
    string? FailureError,
    string ResponseJson,
    string AttemptBy,
    DateTime AttemptOn);

public sealed record RequestLogDetailsDto(RequestLogDto Log, string? Json, IReadOnlyList<RequestAttemptDto> Attempts);

/// <param name="From">The first UTC day to include.</param>
/// <param name="To">The last UTC day to include.</param>
public sealed record RequestLogFilter(long? ClientRefId, string? Status, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50);

public sealed record RequestLogPage(IReadOnlyList<RequestLogDto> Logs, int Total, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}
