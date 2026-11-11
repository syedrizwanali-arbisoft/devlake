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

/// <summary>An active OAuth2 config for client 1, with an encrypted secret of "v1:enc" and three delivery attempts.</summary>
public sealed class ClientConfigBuilder
{
    private readonly ClientConfig _config = new()
    {
        Id = 5,
        ClientRefId = 1,
        AuthType = ClientConfigAuthTypes.OAuth2ClientCredentials,
        BaseUrl = "https://sophia.test/",
        TokenUrl = "/oauth2/token/",
        OutboundClientId = "devlake",
        OutboundClientSecretEncrypted = "v1:enc",
        OutboundScope = "s",
        TokenRotatedOn = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        MaxAttempts = 3,
        IsActive = true,
        CreatedOn = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "first",
    };

    /// <summary>Switches to a static API token stored as <paramref name="encryptedToken"/>, clearing the OAuth2 fields.</summary>
    public ClientConfigBuilder WithStaticToken(string encryptedToken = "v1:tok")
    {
        _config.AuthType = ClientConfigAuthTypes.ApiToken;
        _config.ApiTokenEncrypted = encryptedToken;
        _config.TokenUrl = null;
        _config.OutboundClientId = null;
        _config.OutboundClientSecretEncrypted = null;
        _config.OutboundScope = null;
        return this;
    }

    public ClientConfigBuilder WithEncryptedSecret(string encryptedSecret)
    {
        _config.OutboundClientSecretEncrypted = encryptedSecret;
        return this;
    }

    public ClientConfigBuilder WithMaxAttempts(int maxAttempts)
    {
        _config.MaxAttempts = maxAttempts;
        return this;
    }

    public ClientConfigBuilder Inactive()
    {
        _config.IsActive = false;
        return this;
    }

    public ClientConfig Build() => _config;
}
