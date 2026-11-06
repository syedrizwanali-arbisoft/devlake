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

/// <summary>How the service authenticates when it sends data to a client.</summary>
public sealed class ClientConfig
{
    public long Id { get; set; }
    public long ClientRefId { get; set; }
    public string AuthType { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? TokenUrl { get; set; }
    public string? OutboundClientId { get; set; }
    public string? OutboundClientSecretEncrypted { get; set; }
    public string? OutboundScope { get; set; }
    public string? ApiTokenEncrypted { get; set; }
    public DateTime? TokenExpiresOn { get; set; }
    public DateTime? TokenRotatedOn { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
