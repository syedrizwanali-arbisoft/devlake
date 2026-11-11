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

/// <summary>A bearer token the Auth API issued. Setting RevokedOn invalidates it before it expires.</summary>
public sealed class AccessToken
{
    public long Id { get; init; }
    public string Jti { get; init; } = string.Empty;
    public long ClientRefId { get; init; }
    public long ClientSecretId { get; init; }
    public long SigningKeyId { get; init; }
    public string GrantedScopes { get; init; } = string.Empty;
    public DateTimeOffset IssuedOn { get; init; }
    public DateTimeOffset ExpiresOn { get; init; }
    public DateTimeOffset? RevokedOn { get; init; }
    public string? RevokedReason { get; init; }
    public string IssuedToIp { get; init; } = string.Empty;
}
