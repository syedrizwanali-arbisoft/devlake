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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;

/// <summary>An active token for client 1, issued an hour before <see cref="Now"/> and valid for two hours.</summary>
public sealed class AccessTokenBuilder
{
    public static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly AccessToken _token = new()
    {
        Id = 1,
        Jti = "jti-1",
        ClientRefId = 1,
        ClientSecretId = 10,
        SigningKeyId = 5,
        GrantedScopes = "stats:read",
        IssuedOn = Now.AddHours(-1),
        ExpiresOn = Now.AddHours(1),
        IssuedToIp = "10.0.0.1",
    };

    public AccessTokenBuilder WithJti(string jti)
    {
        _token.Jti = jti;
        return this;
    }

    public AccessTokenBuilder ForClient(long clientRefId)
    {
        _token.ClientRefId = clientRefId;
        return this;
    }

    public AccessTokenBuilder Expired()
    {
        _token.ExpiresOn = Now.AddHours(-1);
        return this;
    }

    public AccessTokenBuilder Revoked(string? reason = null)
    {
        _token.RevokedOn = Now;
        _token.RevokedReason = reason;
        return this;
    }

    public AccessToken Build() => _token;
}
