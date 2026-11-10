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

using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class CallbackTargetResolverTests
{
    private readonly IClientConfigRepository _configs = Substitute.For<IClientConfigRepository>();
    private readonly ISecretEncryptor _encryptor = Substitute.For<ISecretEncryptor>();
    private readonly CallbackTargetResolver _resolver;
    private readonly ClientConfig _config = new()
    {
        Id = 5, ClientRefId = 1, AuthType = ClientConfigAuthTypes.OAuth2ClientCredentials, BaseUrl = "https://sophia.test/",
        TokenUrl = "/oauth2/token/", OutboundClientId = "devlake", OutboundClientSecretEncrypted = "v1:enc", OutboundScope = "s",
        MaxAttempts = 3, IsActive = true,
    };

    public CallbackTargetResolverTests()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(_config);
        _configs.GetActiveOutboundUrlAsync(1, "engineer-stats", Arg.Any<CancellationToken>())
            .Returns(new ClientConfigOutboundUrl { Path = "/api/v1/devlake/callback/", Method = "POST", IsActive = true });
        _encryptor.Decrypt("v1:enc").Returns("plain-secret");
        _encryptor.Decrypt("v1:tok").Returns("plain-token");
        _resolver = new CallbackTargetResolver(_configs, _encryptor);
    }

    [Fact]
    public async Task Resolve_BuildsTheConnectionWithTheDecryptedSecret()
    {
        var target = await _resolver.ResolveAsync(1, "engineer-stats");

        Assert.Equal(new CallbackTarget(
            new SophiaConnection(5, "https://sophia.test/", new OAuth2ClientCredentials("/oauth2/token/", "devlake", "plain-secret", "s")),
            "POST", "/api/v1/devlake/callback/", 3), target);
    }

    [Fact]
    public async Task Resolve_SupportsAStaticApiToken()
    {
        _config.AuthType = ClientConfigAuthTypes.ApiToken;
        _config.ApiTokenEncrypted = "v1:tok";

        var target = await _resolver.ResolveAsync(1, "engineer-stats");

        Assert.Equal(new StaticApiToken("plain-token"), target!.Connection.Auth);
    }

    [Fact]
    public async Task Resolve_ReturnsNullWithoutAnActiveConfigOrCallback()
    {
        Assert.Null(await _resolver.ResolveAsync(1, "engineer-stats-batch"));
        _config.IsActive = false;
        Assert.Null(await _resolver.ResolveAsync(1, "engineer-stats"));
        Assert.Null(await _resolver.ResolveAsync(2, "engineer-stats"));
    }

    [Fact]
    public async Task Resolve_UsesAtLeastOneAttempt()
    {
        _config.MaxAttempts = 0;

        Assert.Equal(1, (await _resolver.ResolveAsync(1, "engineer-stats"))!.MaxAttempts);
    }

    [Theory]
    [InlineData("tokenUrl")]
    [InlineData("outboundClientId")]
    [InlineData("outboundClientSecretEncrypted")]
    public async Task Resolve_NamesAMissingOAuthColumn(string column)
    {
        switch (column)
        {
            case "tokenUrl": _config.TokenUrl = null; break;
            case "outboundClientId": _config.OutboundClientId = ""; break;
            default: _config.OutboundClientSecretEncrypted = null; break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.ResolveAsync(1, "engineer-stats"));
        Assert.Contains(column, exception.Message);
    }

    [Fact]
    public async Task Resolve_RejectsAnUnknownAuthType()
    {
        _config.AuthType = "mtls";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.ResolveAsync(1, "engineer-stats"));
    }
}
