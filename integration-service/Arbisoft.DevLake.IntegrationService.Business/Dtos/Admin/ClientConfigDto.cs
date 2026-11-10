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

/// <summary>A client's outbound settings. Secrets are never returned, only whether one is stored.</summary>
public sealed record ClientConfigDto(
    long Id,
    long ClientRefId,
    string AuthType,
    string BaseUrl,
    string? TokenUrl,
    string? OutboundClientId,
    bool HasOutboundClientSecret,
    string? OutboundScope,
    bool HasApiToken,
    DateTime? TokenExpiresOn,
    DateTime? TokenRotatedOn,
    int MaxAttempts,
    bool IsActive,
    DateTime CreatedOn,
    string CreatedBy,
    DateTime? ModifiedOn,
    string? ModifiedBy);

/// <summary>
/// The editable outbound settings. A null or empty secret keeps the stored one; a new value replaces it and records
/// the rotation date.
/// </summary>
public sealed record ClientConfigInput(
    string AuthType,
    string BaseUrl,
    string? TokenUrl,
    string? OutboundClientId,
    string? OutboundClientSecret,
    string? OutboundScope,
    string? ApiToken,
    DateTime? TokenExpiresOn,
    int MaxAttempts,
    bool IsActive);
