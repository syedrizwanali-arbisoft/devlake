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
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class ClientConfigServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly IClientConfigRepository _configs = Substitute.For<IClientConfigRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly ISecretEncryptor _encryptor = Substitute.For<ISecretEncryptor>();
    private readonly ClientConfigService _service;
    private ClientConfig? _saved;

    public ClientConfigServiceTests()
    {
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        _configs.GetByClientRefIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((ClientConfig?)null);
        _configs.AddAsync(Arg.Do<ClientConfig>(c => _saved = c), Arg.Any<CancellationToken>()).Returns(5L);
        _configs.UpdateAsync(Arg.Do<ClientConfig>(c => _saved = c), Arg.Any<CancellationToken>()).Returns(true);
        _configs.GetOutboundUrlsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns([]);
        _configs.AddOutboundUrlAsync(Arg.Any<ClientConfigOutboundUrl>(), Arg.Any<CancellationToken>()).Returns(9L);
        _encryptor.Encrypt(Arg.Any<string>()).Returns(call => "enc(" + call.Arg<string>() + ")");
        _service = new ClientConfigService(_configs, _clients, _encryptor, new FakeTimeProvider(new DateTimeOffset(Now)));
    }

    private static ClientConfigInput OAuth(string? secret = "s3cret") => new(
        "oauth2", "https://sophia.test/", "/oauth2/token/", "devlake-stage", secret, "stats.write", null, null, 3, true);

    private ClientConfig Existing(string authType = "oauth2") => new()
    {
        Id = 5, ClientRefId = 1, AuthType = authType, BaseUrl = "https://old.test/", TokenUrl = "/t", OutboundClientId = "old",
        OutboundClientSecretEncrypted = authType == "oauth2" ? "enc(old)" : null, ApiTokenEncrypted = authType == "api_token" ? "enc(tok)" : null,
        TokenRotatedOn = Now.AddDays(-30), MaxAttempts = 2, IsActive = true, CreatedOn = Now.AddDays(-60), CreatedBy = "first",
    };

    [Fact]
    public async Task Save_CreatesAConfigWithTheSecretEncrypted()
    {
        var result = await _service.SaveAsync(1, OAuth(), "admin");

        Assert.True(result.Succeeded);
        Assert.Equal("enc(s3cret)", _saved!.OutboundClientSecretEncrypted);
        Assert.Equal(Now, _saved.TokenRotatedOn);
        Assert.Equal("admin", _saved.CreatedBy);
        Assert.Equal(Now, _saved.CreatedOn);
        Assert.Equal("https://sophia.test/", _saved.BaseUrl);
        await _configs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Save_KeepsTheStoredSecretWhenLeftBlank()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());

        var result = await _service.SaveAsync(1, OAuth(secret: "  "), "admin2");

        Assert.True(result.Succeeded);
        Assert.Equal("enc(old)", _saved!.OutboundClientSecretEncrypted);
        Assert.Equal(Now.AddDays(-30), _saved.TokenRotatedOn);
        Assert.Equal("admin2", _saved.ModifiedBy);
        Assert.Equal("first", _saved.CreatedBy);
        _encryptor.DidNotReceiveWithAnyArgs().Encrypt(default!);
    }

    [Fact]
    public async Task Save_SwitchingToAStaticTokenRemovesTheOAuthSecret()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());

        var result = await _service.SaveAsync(1, new ClientConfigInput("api_token", "https://x.test/", null, null, null, null, "tok", null, 3, true), "admin");

        Assert.True(result.Succeeded);
        Assert.Equal("enc(tok)", _saved!.ApiTokenEncrypted);
        Assert.Null(_saved.OutboundClientSecretEncrypted);
        Assert.Null(_saved.OutboundClientId);
        Assert.Null(_saved.TokenUrl);
    }

    [Fact]
    public async Task Save_StoresTheExpiryAsUtc()
    {
        await _service.SaveAsync(1, OAuth() with { TokenExpiresOn = new DateTime(2027, 1, 31) }, "admin");

        Assert.Equal(DateTimeKind.Utc, _saved!.TokenExpiresOn!.Value.Kind);
        Assert.Equal(new DateTime(2027, 1, 31), _saved.TokenExpiresOn.Value);
    }

    [Theory]
    [InlineData("mtls", "https://x.test/", "/t", "id", "s", 3, "auth type")]
    [InlineData("oauth2", "not a url", "/t", "id", "s", 3, "base URL")]
    [InlineData("oauth2", "ftp://x.test/", "/t", "id", "s", 3, "base URL")]
    [InlineData("oauth2", "https://x.test/", "", "id", "s", 3, "token URL")]
    [InlineData("oauth2", "https://x.test/", "/t", "", "s", 3, "client ID")]
    [InlineData("oauth2", "https://x.test/", "/t", "id", "", 3, "secret")]
    [InlineData("oauth2", "https://x.test/", "/t", "id", "s", 0, "Max attempts")]
    [InlineData("oauth2", "https://x.test/", "/t", "id", "s", 11, "Max attempts")]
    [InlineData("api_token", "https://x.test/", null, null, null, 3, "API token")]
    public async Task Save_RejectsInvalidInput(string auth, string baseUrl, string? tokenUrl, string? clientId, string? secret, int attempts, string mentions)
    {
        var result = await _service.SaveAsync(1, new ClientConfigInput(auth, baseUrl, tokenUrl, clientId, secret, null, null, null, attempts, true), "admin");

        Assert.False(result.Succeeded);
        Assert.Contains(mentions, result.Error);
        await _configs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Get_NeverReturnsSecrets()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());

        var dto = await _service.GetAsync(1);

        Assert.True(dto!.HasOutboundClientSecret);
        Assert.False(dto.HasApiToken);
        Assert.DoesNotContain("enc(", dto.ToString());
    }

    [Fact]
    public async Task AddOutboundUrl_NeedsAConfigFirst()
    {
        var result = await _service.AddOutboundUrlAsync(1, new OutboundUrlInput("engineer-stats", "POST", "/cb", true), "admin");

        Assert.Contains("outbound config first", result.Error);
    }

    [Fact]
    public async Task AddOutboundUrl_SavesARelativePath()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());

        var result = await _service.AddOutboundUrlAsync(1, new OutboundUrlInput(" engineer-stats ", "POST", " /api/v1/devlake/callback/ ", true), "admin");

        Assert.Equal(AdminResult<long>.Ok(9), result);
        await _configs.Received(1).AddOutboundUrlAsync(Arg.Is<ClientConfigOutboundUrl>(u => u.ClientConfigId == 5 && u.EventType == "engineer-stats"
            && u.Path == "/api/v1/devlake/callback/" && u.CreatedBy == "admin"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("engineer-stats", "GET", "/cb", "method")]
    [InlineData("", "POST", "/cb", "event type")]
    [InlineData("engineer-stats", "POST", "https://evil.example/cb", "relative")]
    [InlineData("engineer-stats", "POST", "//evil.example/cb", "relative")]
    [InlineData("engineer-stats", "POST", "", "relative")]
    public async Task AddOutboundUrl_RejectsInvalidInput(string eventType, string method, string path, string mentions)
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());

        var result = await _service.AddOutboundUrlAsync(1, new OutboundUrlInput(eventType, method, path, true), "admin");

        Assert.Contains(mentions, result.Error);
    }

    [Fact]
    public async Task OutboundUrls_AllowOnlyOneActiveUrlPerEventType()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());
        _configs.GetOutboundUrlsAsync(5, Arg.Any<CancellationToken>()).Returns([
            new ClientConfigOutboundUrl { Id = 1, ClientConfigId = 5, EventType = "engineer-stats", Path = "/a", Method = "POST", IsActive = true },
            new ClientConfigOutboundUrl { Id = 2, ClientConfigId = 5, EventType = "engineer-stats", Path = "/b", Method = "POST", IsActive = false }]);

        Assert.Contains("already an active URL", (await _service.AddOutboundUrlAsync(1, new OutboundUrlInput("engineer-stats", "POST", "/c", true), "a")).Error);
        Assert.True((await _service.AddOutboundUrlAsync(1, new OutboundUrlInput("engineer-stats", "POST", "/c", false), "a")).Succeeded);
        Assert.True((await _service.UpdateOutboundUrlAsync(1, 1, new OutboundUrlInput("engineer-stats", "PUT", "/a2", true), "a")).Succeeded);
        Assert.Contains("already an active URL", (await _service.UpdateOutboundUrlAsync(1, 2, new OutboundUrlInput("engineer-stats", "POST", "/b", true), "a")).Error);
    }

    [Fact]
    public async Task UpdateOutboundUrl_RejectsAnotherClientsUrl()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Existing());

        Assert.Contains("not found", (await _service.UpdateOutboundUrlAsync(1, 99, new OutboundUrlInput("e", "POST", "/x", true), "a")).Error);
    }
}
