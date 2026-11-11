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

/// <summary>Builds an <see cref="AccessToken"/>: active for an hour by default.</summary>
public sealed class AccessTokenBuilder
{
    private static readonly DateTimeOffset DefaultIssuedOn = new(2026, 11, 11, 11, 0, 0, TimeSpan.Zero);

    private long _id = 1;
    private string _jti = Guid.NewGuid().ToString();
    private long _clientRefId = 1;
    private long _clientSecretId = 1;
    private long _signingKeyId = 1;
    private string _scopes = "stats:read";
    private DateTimeOffset _issuedOn = DefaultIssuedOn;
    private DateTimeOffset _expiresOn = DefaultIssuedOn.AddHours(1);
    private DateTimeOffset? _revokedOn;
    private string? _revokedReason;
    private string _issuedToIp = "203.0.113.7";

    public AccessTokenBuilder WithId(long id) => Set(() => _id = id);

    public AccessTokenBuilder WithJti(string jti) => Set(() => _jti = jti);

    public AccessTokenBuilder ForClient(long clientRefId) => Set(() => _clientRefId = clientRefId);

    public AccessTokenBuilder IssuedWith(long clientSecretId, long signingKeyId) => Set(() => (_clientSecretId, _signingKeyId) = (clientSecretId, signingKeyId));

    public AccessTokenBuilder WithScopes(string scopes) => Set(() => _scopes = scopes);

    public AccessTokenBuilder Issued(DateTimeOffset issuedOn, TimeSpan lifetime) => Set(() => (_issuedOn, _expiresOn) = (issuedOn, issuedOn + lifetime));

    public AccessTokenBuilder Revoked(DateTimeOffset revokedOn, string reason) => Set(() => (_revokedOn, _revokedReason) = (revokedOn, reason));

    public AccessToken Build() => new()
    {
        Id = _id,
        Jti = _jti,
        ClientRefId = _clientRefId,
        ClientSecretId = _clientSecretId,
        SigningKeyId = _signingKeyId,
        GrantedScopes = _scopes,
        IssuedOn = _issuedOn,
        ExpiresOn = _expiresOn,
        RevokedOn = _revokedOn,
        RevokedReason = _revokedReason,
        IssuedToIp = _issuedToIp
    };

    private AccessTokenBuilder Set(Action change)
    {
        change();
        return this;
    }
}
