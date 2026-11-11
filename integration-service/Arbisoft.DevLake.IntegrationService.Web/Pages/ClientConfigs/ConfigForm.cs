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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.ClientConfigs;

/// <summary>The fields of the outbound config form. The secret and token are write-only: blank keeps what is stored.</summary>
public sealed class ConfigForm
{
    public string AuthType { get; set; } = ClientConfigAuthType.OAuth2ClientCredentials;

    public string BaseUrl { get; set; } = string.Empty;

    public string? TokenUrl { get; set; }

    public string? OutboundClientId { get; set; }

    public string? OutboundClientSecret { get; set; }

    public string? OutboundScope { get; set; }

    public string? ApiToken { get; set; }

    public DateOnly? TokenExpiresOn { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public bool IsActive { get; set; } = true;

    public ClientConfigInput ToInput() => new(
        AuthType,
        BaseUrl,
        TokenUrl,
        OutboundClientId,
        OutboundClientSecret,
        OutboundScope,
        ApiToken,
        TokenExpiresOn is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
        MaxAttempts,
        IsActive);
}
