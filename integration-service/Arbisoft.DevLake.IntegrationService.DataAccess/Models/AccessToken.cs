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

/// <summary>A bearer token issued by the Auth API.</summary>
public sealed class AccessToken
{
    public long Id { get; set; }
    public string Jti { get; set; } = string.Empty;
    public long ClientRefId { get; set; }
    public long ClientSecretId { get; set; }
    public long SigningKeyId { get; set; }
    public string GrantedScopes { get; set; } = string.Empty;
    public DateTime IssuedOn { get; set; }
    public DateTime ExpiresOn { get; set; }
    public DateTime? RevokedOn { get; set; }
    public string? RevokedReason { get; set; }
    public string IssuedToIp { get; set; } = string.Empty;
}
