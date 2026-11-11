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

/// <summary>
/// A client's outbound configuration as the portal shows it. The secrets are never read back: <see cref="HasClientSecret"/> and
/// <see cref="HasApiToken"/> only say whether one is stored.
/// </summary>
public sealed record ClientConfigDto(
    long Id,
    string AuthType,
    string BaseUrl,
    string? TokenUrl,
    string? OutboundClientId,
    string? OutboundScope,
    bool HasClientSecret,
    bool HasApiToken,
    DateTimeOffset? TokenExpiresOn,
    DateTimeOffset? TokenRotatedOn,
    int MaxAttempts,
    bool IsActive,
    DateTimeOffset CreatedOn,
    string CreatedBy,
    DateTimeOffset? ModifiedOn,
    string? ModifiedBy);

/// <summary>A client with its outbound configuration, or none yet.</summary>
public sealed record ClientConfigRowDto(long ClientRefId, string ClientId, string ClientName, ClientConfigDto? Config);

public sealed record ClientConfigDetailsDto(long ClientRefId, string ClientId, string ClientName, ClientConfigDto? Config, IReadOnlyList<OutboundUrlDto> Urls);

/// <summary>
/// What the admin submits. A blank <see cref="OutboundClientSecret"/> or <see cref="ApiToken"/> keeps the stored value; a filled one replaces it
/// and counts as a rotation.
/// </summary>
public sealed record ClientConfigInput(
    string AuthType,
    string BaseUrl,
    string? TokenUrl,
    string? OutboundClientId,
    string? OutboundClientSecret,
    string? OutboundScope,
    string? ApiToken,
    DateTimeOffset? TokenExpiresOn,
    int MaxAttempts,
    bool IsActive);

public sealed record OutboundUrlDto(
    long Id,
    long ClientConfigId,
    string EventType,
    string Method,
    string Path,
    bool IsActive,
    DateTimeOffset CreatedOn,
    string CreatedBy,
    DateTimeOffset? ModifiedOn,
    string? ModifiedBy);

public sealed record OutboundUrlInput(string EventType, string Method, string Path, bool IsActive);
