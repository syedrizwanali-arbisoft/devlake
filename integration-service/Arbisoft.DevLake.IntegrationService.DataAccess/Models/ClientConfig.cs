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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Models;

/// <summary>How the service authenticates when it sends data to a client. Secrets are stored encrypted.</summary>
public sealed class ClientConfig
{
    public long Id { get; init; }
    public long ClientRefId { get; init; }
    public string AuthType { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? TokenUrl { get; init; }
    public string? OutboundClientId { get; init; }
    public string? OutboundClientSecretEncrypted { get; init; }
    public string? OutboundScope { get; init; }
    public string? ApiTokenEncrypted { get; init; }
    public DateTimeOffset? TokenExpiresOn { get; init; }
    public DateTimeOffset? TokenRotatedOn { get; init; }
    public int MaxAttempts { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTimeOffset? ModifiedOn { get; init; }
    public string? ModifiedBy { get; init; }
}
