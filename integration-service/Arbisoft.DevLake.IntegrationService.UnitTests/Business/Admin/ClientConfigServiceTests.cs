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
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class ClientConfigServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string OAuth = ClientConfigAuthType.OAuth2ClientCredentials;
    private const string Token = ClientConfigAuthType.ApiToken;

    private readonly IClientConfigRepository _configs = Substitute.For<IClientConfigRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly ISecretEncryptor _encryptor = Substitute.For<ISecretEncryptor>();
    private readonly ClientConfigService _service;

    public ClientConfigServiceTests()
    {
        _encryptor.Encrypt(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Client { Id = 1, ClientId = "sophia", Name = "Sophia" });
        _service = new ClientConfigService(_configs, _clients, _encryptor, new FakeTimeProvider(Now));
    }

    private static ClientConfigInput OAuthInput(string? secret = "s3cret", string baseUrl = "https://sophia.example.com", int attempts = 3) =>
        new(OAuth, baseUrl, "https://sophia.example.com/oauth2/token/", "devlake", secret, "callback", null, null, attempts, true);

    private static ClientConfigInput TokenInput(string? token = "tok", DateTimeOffset? expires = null) =>
        new(Token, "https://sophia.example.com", null, null, null, null, token, expires, 3, true);

    private static ClientConfig Stored(string authType = OAuth) => new()
    {
        Id = 4,
        ClientRefId = 1,
        AuthType = authType,
        BaseUrl = "https://sophia.example.com",
        TokenUrl = "https://sophia.example.com/oauth2/token/",
        OutboundClientId = "devlake",
        OutboundClientSecretEncrypted = authType == OAuth ? "enc:old" : null,
        ApiTokenEncrypted = authType == Token ? "enc:oldtoken" : null,
        TokenRotatedOn = Now.AddDays(-30),
        MaxAttempts = 3,
        IsActive = true,
        CreatedOn = Now.AddDays(-60),
        CreatedBy = "first"
    };

    [Fact]
    public async Task Save_ANewOAuthConfig_EncryptsTheSecret_AndRecordsWhoAndWhen()
    {
        var result = await _service.SaveAsync(1, OAuthInput(), "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        await _configs.Received(1).InsertAsync(
            Arg.Is<ClientConfig>(c => c.ClientRefId == 1 && c.AuthType == OAuth && c.OutboundClientSecretEncrypted == "enc:s3cret" && c.ApiTokenEncrypted == null
                && c.TokenRotatedOn == Now && c.CreatedBy == "admin" && c.CreatedOn == Now && c.ModifiedBy == null),
            Arg.Any<CancellationToken>());
        await _configs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Save_ANewConfigWithoutASecret_IsRefused()
    {
        var result = await _service.SaveAsync(1, OAuthInput(secret: " "), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _configs.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Save_ABlankSecretOnAnExistingConfig_KeepsTheStoredSecretAndTheRotationDate()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Stored());

        var result = await _service.SaveAsync(1, OAuthInput(secret: null, attempts: 5), "editor", CancellationToken.None);

        Assert.True(result.Succeeded);
        await _configs.Received(1).UpdateAsync(
            Arg.Is<ClientConfig>(c => c.Id == 4 && c.OutboundClientSecretEncrypted == "enc:old" && c.TokenRotatedOn == Now.AddDays(-30) && c.MaxAttempts == 5
                && c.ModifiedBy == "editor" && c.ModifiedOn == Now && c.CreatedBy == "first" && c.CreatedOn == Now.AddDays(-60)),
            Arg.Any<CancellationToken>());
        _encryptor.DidNotReceiveWithAnyArgs().Encrypt(default!);
    }

    [Fact]
    public async Task Save_ANewSecretOnAnExistingConfig_ReplacesItAndRecordsTheRotation()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Stored());

        await _service.SaveAsync(1, OAuthInput(secret: "fresh"), "editor", CancellationToken.None);

        await _configs.Received(1).UpdateAsync(Arg.Is<ClientConfig>(c => c.OutboundClientSecretEncrypted == "enc:fresh" && c.TokenRotatedOn == Now), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_SwitchingToAStaticToken_ClearsTheOAuthFields_AndNeedsANewToken()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Stored());

        var withoutToken = await _service.SaveAsync(1, TokenInput(token: null), "editor", CancellationToken.None);
        Assert.False(withoutToken.Succeeded);

        var result = await _service.SaveAsync(1, TokenInput("tok", Now.AddDays(90)), "editor", CancellationToken.None);

        Assert.True(result.Succeeded);
        await _configs.Received(1).UpdateAsync(
            Arg.Is<ClientConfig>(c => c.AuthType == Token && c.ApiTokenEncrypted == "enc:tok" && c.OutboundClientSecretEncrypted == null && c.TokenUrl == null
                && c.OutboundClientId == null && c.TokenExpiresOn == Now.AddDays(90) && c.TokenRotatedOn == Now),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("http://sophia.example.com")]
    [InlineData("ftp://sophia.example.com")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task Save_ABaseUrlThatIsNotHttps_IsRefused(string baseUrl)
    {
        var result = await _service.SaveAsync(1, OAuthInput(baseUrl: baseUrl), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _configs.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Save_PlainHttpToLocalhost_IsAllowedForDevelopment()
    {
        var result = await _service.SaveAsync(1, OAuthInput(baseUrl: "http://localhost:5097"), "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Save_MaxAttemptsOutOfRange_IsRefused(int attempts)
    {
        var result = await _service.SaveAsync(1, OAuthInput(attempts: attempts), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Save_OAuthWithoutATokenUrlOrClientId_IsRefused()
    {
        Assert.False((await _service.SaveAsync(1, OAuthInput() with { TokenUrl = null }, "admin", CancellationToken.None)).Succeeded);
        Assert.False((await _service.SaveAsync(1, OAuthInput() with { OutboundClientId = " " }, "admin", CancellationToken.None)).Succeeded);
        Assert.False((await _service.SaveAsync(1, OAuthInput() with { AuthType = "Basic" }, "admin", CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Save_ForAnUnknownClient_Fails()
    {
        var result = await _service.SaveAsync(9, OAuthInput(), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Get_NeverReturnsASecret_OnlyWhetherOneIsStored()
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Stored());

        var details = await _service.GetAsync(1, CancellationToken.None);

        var config = details!.Config!;
        Assert.True(config.HasClientSecret);
        Assert.False(config.HasApiToken);
        Assert.DoesNotContain(typeof(ClientConfigDto).GetProperties(), p => p.Name.Contains("Encrypted", StringComparison.Ordinal) || p.Name is "OutboundClientSecret" or "ApiToken");
    }

    [Fact]
    public async Task Get_AClientWithNoConfig_ShowsNone_AndAnUnknownClientIsNull()
    {
        var details = await _service.GetAsync(1, CancellationToken.None);

        Assert.Null(details!.Config);
        Assert.Empty(details.Urls);
        Assert.Null(await _service.GetAsync(9, CancellationToken.None));
    }

    [Fact]
    public async Task List_PairsEachClientWithItsConfigOrNone()
    {
        _clients.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Client>([new Client { Id = 1, ClientId = "sophia" }, new Client { Id = 2, ClientId = "other" }], 2, 1, 200));
        _configs.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<ClientConfig>([Stored()], 1, 1, 200));

        var rows = await _service.ListAsync(CancellationToken.None);

        Assert.NotNull(rows[0].Config);
        Assert.Null(rows[1].Config);
    }

    private void GivenConfig(params ClientConfigOutboundUrl[] urls)
    {
        _configs.GetByClientRefIdAsync(1, Arg.Any<CancellationToken>()).Returns(Stored());
        _configs.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(Stored());
        _configs.GetOutboundUrlsAsync(4, Arg.Any<CancellationToken>()).Returns(urls);
    }

    private static OutboundUrlInput UrlInput(string path = "/api/v1/devlake/callback/", string eventType = "engineer-stats", string method = "POST", bool active = true) =>
        new(eventType, method, path, active);

    [Fact]
    public async Task CreateUrl_StoresItWithWhoCreatedIt()
    {
        GivenConfig();
        _configs.InsertOutboundUrlAsync(Arg.Any<ClientConfigOutboundUrl>(), Arg.Any<CancellationToken>()).Returns(8L);

        var result = await _service.CreateUrlAsync(1, UrlInput(), "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(8, result.Value);
        await _configs.Received(1).InsertOutboundUrlAsync(
            Arg.Is<ClientConfigOutboundUrl>(u => u.ClientConfigId == 4 && u.EventType == "engineer-stats" && u.Method == "POST" && u.Path == "/api/v1/devlake/callback/" && u.CreatedBy == "admin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUrl_BeforeTheConfigExists_IsRefused()
    {
        var result = await _service.CreateUrlAsync(1, UrlInput(), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("//evil.example.com/x")]
    [InlineData("/ok\\bad")]
    [InlineData("")]
    public async Task CreateUrl_ABadPath_IsRefused(string path)
    {
        GivenConfig();

        var result = await _service.CreateUrlAsync(1, UrlInput(path), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _configs.DidNotReceiveWithAnyArgs().InsertOutboundUrlAsync(default!, default);
    }

    [Theory]
    [InlineData("unknown-event", "POST")]
    [InlineData("engineer-stats", "GET")]
    public async Task CreateUrl_AnUnknownEventTypeOrMethod_IsRefused(string eventType, string method)
    {
        GivenConfig();

        var result = await _service.CreateUrlAsync(1, UrlInput(eventType: eventType, method: method), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateUrl_ASecondActiveUrlForTheSameEvent_IsRefused_ButAnInactiveOneIsFine()
    {
        GivenConfig(new ClientConfigOutboundUrl { Id = 7, ClientConfigId = 4, EventType = "engineer-stats", IsActive = true });

        Assert.False((await _service.CreateUrlAsync(1, UrlInput(), "admin", CancellationToken.None)).Succeeded);
        Assert.True((await _service.CreateUrlAsync(1, UrlInput(active: false), "admin", CancellationToken.None)).Succeeded);
        Assert.True((await _service.CreateUrlAsync(1, UrlInput(eventType: "engineer-stats-batch"), "admin", CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task UpdateUrl_CanKeepItselfActive_ButNotClashWithAnother()
    {
        GivenConfig(
            new ClientConfigOutboundUrl { Id = 7, ClientConfigId = 4, EventType = "engineer-stats", IsActive = true },
            new ClientConfigOutboundUrl { Id = 8, ClientConfigId = 4, EventType = "engineer-stats-batch", IsActive = true });
        _configs.GetOutboundUrlByIdAsync(7, Arg.Any<CancellationToken>()).Returns(new ClientConfigOutboundUrl { Id = 7, ClientConfigId = 4, CreatedBy = "first", CreatedOn = Now.AddDays(-2) });

        var ok = await _service.UpdateUrlAsync(7, UrlInput("/new/path/"), "editor", CancellationToken.None);
        var clash = await _service.UpdateUrlAsync(7, UrlInput(eventType: "engineer-stats-batch"), "editor", CancellationToken.None);

        Assert.True(ok.Succeeded);
        Assert.False(clash.Succeeded);
        await _configs.Received(1).UpdateOutboundUrlAsync(
            Arg.Is<ClientConfigOutboundUrl>(u => u.Id == 7 && u.Path == "/new/path/" && u.ModifiedBy == "editor" && u.ModifiedOn == Now && u.CreatedBy == "first"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateUrl_AnUnknownUrl_Fails()
    {
        var result = await _service.UpdateUrlAsync(99, UrlInput(), "editor", CancellationToken.None);

        Assert.False(result.Succeeded);
    }
}
