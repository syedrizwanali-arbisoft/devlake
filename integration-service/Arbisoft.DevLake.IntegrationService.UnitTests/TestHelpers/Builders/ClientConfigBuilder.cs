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

/// <summary>Builds a <see cref="ClientConfig"/>: an active OAuth2 client-credentials config with a stored (already "encrypted") secret.</summary>
public sealed class ClientConfigBuilder
{
    private long _id = 4;
    private long _clientRefId = 1;
    private string _authType = ClientConfigAuthType.OAuth2ClientCredentials;
    private string _baseUrl = "https://sophia.example.com";
    private string? _tokenUrl = "https://sophia.example.com/oauth2/token/";
    private string? _outboundClientId = "devlake";
    private string? _secretEncrypted = "enc:secret";
    private string? _scope;
    private string? _tokenEncrypted;
    private DateTimeOffset? _tokenExpiresOn;
    private DateTimeOffset? _rotatedOn = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private int _maxAttempts = 3;
    private bool _isActive = true;
    private DateTimeOffset _createdOn = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
    private string _createdBy = "first";

    public ClientConfigBuilder WithId(long id) => Set(() => _id = id);

    public ClientConfigBuilder ForClient(long clientRefId) => Set(() => _clientRefId = clientRefId);

    public ClientConfigBuilder WithBaseUrl(string baseUrl) => Set(() => _baseUrl = baseUrl);

    public ClientConfigBuilder WithEncryptedSecret(string encryptedSecret) => Set(() => _secretEncrypted = encryptedSecret);

    public ClientConfigBuilder WithScope(string scope) => Set(() => _scope = scope);

    /// <summary>A static API token config: the OAuth fields are cleared, as the portal does when the auth type changes.</summary>
    public ClientConfigBuilder WithApiToken(string encryptedToken, DateTimeOffset? expiresOn = null) => Set(() =>
    {
        _authType = ClientConfigAuthType.ApiToken;
        _tokenEncrypted = encryptedToken;
        _tokenExpiresOn = expiresOn;
        (_tokenUrl, _outboundClientId, _secretEncrypted) = (null, null, null);
    });

    public ClientConfigBuilder WithMaxAttempts(int attempts) => Set(() => _maxAttempts = attempts);

    public ClientConfigBuilder Inactive() => Set(() => _isActive = false);

    public ClientConfigBuilder RotatedOn(DateTimeOffset? rotatedOn) => Set(() => _rotatedOn = rotatedOn);

    public ClientConfigBuilder CreatedOn(DateTimeOffset createdOn, string createdBy) => Set(() => (_createdOn, _createdBy) = (createdOn, createdBy));

    public ClientConfig Build() => new()
    {
        Id = _id,
        ClientRefId = _clientRefId,
        AuthType = _authType,
        BaseUrl = _baseUrl,
        TokenUrl = _tokenUrl,
        OutboundClientId = _outboundClientId,
        OutboundClientSecretEncrypted = _secretEncrypted,
        OutboundScope = _scope,
        ApiTokenEncrypted = _tokenEncrypted,
        TokenExpiresOn = _tokenExpiresOn,
        TokenRotatedOn = _rotatedOn,
        MaxAttempts = _maxAttempts,
        IsActive = _isActive,
        CreatedOn = _createdOn,
        CreatedBy = _createdBy
    };

    private ClientConfigBuilder Set(Action change)
    {
        change();
        return this;
    }
}
