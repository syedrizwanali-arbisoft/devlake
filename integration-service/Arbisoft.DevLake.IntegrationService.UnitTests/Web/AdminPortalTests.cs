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

extern alias web;

using System.Net;
using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Microsoft.AspNetCore.Hosting;
using WebAssemblyMarker = web::Arbisoft.DevLake.IntegrationService.Web.WebAssemblyMarker;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Web;

/// <summary>Runs the real admin portal in memory, with the admin services replaced.</summary>
public sealed partial class AdminPortalTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private static readonly string PasswordHash = new SecretHasher().Hash(Password);

    private readonly IClientService _clients = Substitute.For<IClientService>();
    private readonly IScopeService _scopes = Substitute.For<IScopeService>();
    private readonly ISigningKeyService _signingKeys = Substitute.For<ISigningKeyService>();
    private readonly IAccessTokenService _accessTokens = Substitute.For<IAccessTokenService>();
    private readonly IEmailMappingService _emailMappings = Substitute.For<IEmailMappingService>();
    private readonly IClientConfigService _clientConfigs = Substitute.For<IClientConfigService>();
    private readonly IRequestLogService _requestLogs = Substitute.For<IRequestLogService>();
    private readonly WebApplicationFactory<WebAssemblyMarker> _factory;
    private readonly HttpClient _http;

    public AdminPortalTests()
    {
        _clients.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        _clients.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientDetailsDto(
            new ClientDto(1, "sophia", "Sophia", true, 3600, DateTime.UtcNow, "admin", null, null), [], [], []));
        _factory = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Admin:Username", "admin");
            host.UseSetting("Admin:PasswordHash", PasswordHash);
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(_clients));
                services.Replace(ServiceDescriptor.Singleton(_scopes));
                services.Replace(ServiceDescriptor.Singleton(_signingKeys));
                services.Replace(ServiceDescriptor.Singleton(_accessTokens));
                services.Replace(ServiceDescriptor.Singleton(_emailMappings));
                services.Replace(ServiceDescriptor.Singleton(_clientConfigs));
                services.Replace(ServiceDescriptor.Singleton(_requestLogs));
            });
        });
        _http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }

    private async Task<string> AntiforgeryTokenAsync(string path)
    {
        var html = await _http.GetStringAsync(path);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private Task<HttpResponseMessage> PostFormAsync(string path, params (string Key, string Value)[] fields) =>
        PostFormFromAsync(path, path, fields);

    /// <summary>Takes the antiforgery token from <paramref name="formPage"/>, the page that holds the form.</summary>
    private async Task<HttpResponseMessage> PostFormFromAsync(string formPage, string path, params (string Key, string Value)[] fields)
    {
        var token = await AntiforgeryTokenAsync(formPage);
        return await _http.PostAsync(path, new FormUrlEncodedContent(
            fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", token))));
    }

    private Task<HttpResponseMessage> SignInAsync(string password = Password) =>
        PostFormAsync("/Account/Login", ("Username", "admin"), ("Password", password));

    [Theory]
    [InlineData("/")]
    [InlineData("/Clients")]
    [InlineData("/Clients/Details/1")]
    [InlineData("/Scopes")]
    [InlineData("/SigningKeys")]
    [InlineData("/AccessTokens")]
    [InlineData("/EmailMappings")]
    [InlineData("/RequestLogs")]
    [InlineData("/RequestLogs/Details/1")]
    [InlineData("/ClientConfigs/Edit/1")]
    [InlineData("/ClientConfigs/1/OutboundUrls")]
    public async Task EveryPageRequiresSignIn(string path)
    {
        using var response = await _http.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task SignIn_RejectsAWrongPassword()
    {
        using var response = await SignInAsync("wrong");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The username or password is wrong.", await response.Content.ReadAsStringAsync());
        using var page = await _http.GetAsync("/Clients");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Fact]
    public async Task SignIn_OpensThePortal()
    {
        using var response = await SignInAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("httponly", response.Headers.GetValues("Set-Cookie").First(), StringComparison.OrdinalIgnoreCase);
        using var page = await _http.GetAsync("/Clients");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Sign out (admin)", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignIn_NeverRedirectsOffSite()
    {
        var token = await AntiforgeryTokenAsync("/Account/Login");
        using var response = await _http.PostAsync("/Account/Login?ReturnUrl=https://evil.example/", new FormUrlEncodedContent([
            new("Username", "admin"), new("Password", Password), new("__RequestVerificationToken", token)]));

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Forms_RejectAPostWithoutTheAntiforgeryToken()
    {
        await SignInAsync();

        using var response = await _http.PostAsync("/Scopes/Create", new FormUrlEncodedContent([new("Name", "x:y")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _scopes.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateClient_RecordsTheSignedInAdmin()
    {
        _clients.CreateAsync(default!, default!).ReturnsForAnyArgs(AdminResult<long>.Ok(1));
        await SignInAsync();

        using var response = await PostFormAsync("/Clients/Create",
            ("Form.ClientId", "sophia"), ("Form.Name", "Sophia"), ("Form.AccessTokenLifetimeSeconds", "3600"),
            ("Form.IsActive", "true"), ("Form.IsActive", "false"));

        Assert.Equal("/Clients/Details/1", response.Headers.Location!.OriginalString);
        await _clients.Received(1).CreateAsync(new ClientInput("sophia", "Sophia", true, 3600), "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditClient_CanDeactivateAClient()
    {
        _clients.UpdateAsync(default, default!, default!).ReturnsForAnyArgs(AdminResult.Ok);
        await SignInAsync();

        // An unticked checkbox sends only the hidden "false".
        await PostFormAsync("/Clients/Edit/1",
            ("Form.ClientId", "sophia"), ("Form.Name", "Sophia"), ("Form.AccessTokenLifetimeSeconds", "3600"), ("Form.IsActive", "false"));

        await _clients.Received(1).UpdateAsync(1, Arg.Is<ClientInput>(i => !i.IsActive), "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateClient_ShowsAServiceError()
    {
        _clients.CreateAsync(default!, default!).ReturnsForAnyArgs(AdminResult<long>.Fail("A client with Client ID 'sophia' already exists."));
        await SignInAsync();

        using var response = await PostFormAsync("/Clients/Create",
            ("Form.ClientId", "sophia"), ("Form.Name", "Sophia"), ("Form.AccessTokenLifetimeSeconds", "3600"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task CreateSecret_ShowsTheSecretOnceWithoutCaching()
    {
        _clients.CreateSecretAsync(1, 90, "admin", Arg.Any<CancellationToken>())
            .Returns(AdminResult<NewClientSecretDto>.Ok(new NewClientSecretDto(5, "sophia", "the-plain-secret", DateTime.UtcNow.AddDays(90))));
        await SignInAsync();

        using var response = await PostFormAsync("/Clients/1/Secrets/Create", ("ValidForDays", "90"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("the-plain-secret", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("the-plain-secret", await _http.GetStringAsync("/Clients/Details/1"));
    }

    [Fact]
    public async Task RevokeToken_PassesTheReasonAndAdmin()
    {
        _accessTokens.GetAsync("jti-1", Arg.Any<CancellationToken>()).Returns(
            new AccessTokenDto("jti-1", "sophia", "Sophia", "stats:read", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, null, "::1", "Active"));
        _accessTokens.RevokeAsync(default!, default!, default!).ReturnsForAnyArgs(AdminResult.Ok);
        await SignInAsync();

        using var response = await PostFormAsync("/AccessTokens/Revoke/jti-1", ("Reason", "leaked"));

        Assert.Equal("/AccessTokens", response.Headers.Location!.OriginalString);
        await _accessTokens.Received(1).RevokeAsync("jti-1", "leaked", "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateKey_RunsOnlyOnPost()
    {
        _signingKeys.RotateAsync(default!).ReturnsForAnyArgs(AdminResult<SigningKeyDto>.Ok(
            new SigningKeyDto(2, "key-new", "RS256", "ref", DateTime.UtcNow, null, DateTime.UtcNow, "Active")));
        await SignInAsync();

        await _http.GetAsync("/SigningKeys/Rotate");
        await _signingKeys.DidNotReceiveWithAnyArgs().RotateAsync(default!, default);

        using var response = await PostFormAsync("/SigningKeys/Rotate");
        Assert.Equal("/SigningKeys", response.Headers.Location!.OriginalString);
        await _signingKeys.Received(1).RotateAsync("admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEmailMapping_ShowsTheServiceError()
    {
        _emailMappings.CreateAsync("abc@gmail.com", "x@gmail.com", Arg.Any<CancellationToken>())
            .Returns(AdminResult<long>.Fail("The Arbisoft email must be a valid @arbisoft.com address."));
        await SignInAsync();

        using var response = await PostFormAsync("/EmailMappings/Create", ("ArbisoftEmail", "abc@gmail.com"), ("Email", "x@gmail.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must be a valid @arbisoft.com address", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ClientConfig_PostsTheFormAndNeverEchoesTheSecret()
    {
        _clientConfigs.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientConfigDto(
            5, 1, "oauth2", "https://sophia.test/", "/oauth2/token/", "devlake", true, null, false, null, DateTime.UtcNow, 3, true,
            DateTime.UtcNow, "admin", null, null));
        _clientConfigs.SaveAsync(default, default!, default!).ReturnsForAnyArgs(AdminResult.Ok);
        await SignInAsync();

        var page = await _http.GetStringAsync("/ClientConfigs/Edit/1");
        Assert.Contains("A secret is stored, encrypted. Leave this blank to keep it.", WebUtility.HtmlDecode(page));

        using var response = await PostFormAsync("/ClientConfigs/Edit/1",
            ("Form.AuthType", "oauth2"), ("Form.BaseUrl", "https://sophia.test/"), ("Form.TokenUrl", "/oauth2/token/"),
            ("Form.OutboundClientId", "devlake"), ("Form.OutboundClientSecret", ""), ("Form.MaxAttempts", "4"),
            ("Form.TokenExpiresOn", "2027-01-31"), ("Form.IsActive", "false"));

        Assert.Equal("/Clients/Details/1", response.Headers.Location!.OriginalString);
        await _clientConfigs.Received(1).SaveAsync(1, Arg.Is<ClientConfigInput>(i => i.AuthType == "oauth2" && i.MaxAttempts == 4 && !i.IsActive
            && string.IsNullOrEmpty(i.OutboundClientSecret) && i.TokenExpiresOn == new DateTime(2027, 1, 31)), "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboundUrl_CreateRecordsTheAdmin()
    {
        _clientConfigs.AddOutboundUrlAsync(default, default!, default!).ReturnsForAnyArgs(AdminResult<long>.Ok(9));
        await SignInAsync();

        using var response = await PostFormAsync("/ClientConfigs/1/OutboundUrls/Create",
            ("Form.EventType", "engineer-stats"), ("Form.Method", "POST"), ("Form.Path", "/cb"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));

        Assert.Equal("/ClientConfigs/1/OutboundUrls", response.Headers.Location!.OriginalString);
        await _clientConfigs.Received(1).AddOutboundUrlAsync(1, new OutboundUrlInput("engineer-stats", "POST", "/cb", true), "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestLogs_PassTheFiltersToTheService()
    {
        _requestLogs.SearchAsync(default!).ReturnsForAnyArgs(new RequestLogPage([], 0, 1, 50));
        await SignInAsync();

        using var response = await _http.GetAsync("/RequestLogs?ClientId=1&Status=Failed&From=2026-11-01&To=2026-11-10&PageNumber=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _requestLogs.Received(1).SearchAsync(
            new RequestLogFilter(1, "Failed", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestLogDetails_ShowsTheBodyAndAttemptsAsEscapedText()
    {
        _requestLogs.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new RequestLogDetailsDto(
            new RequestLogDto(1, "corr-1", 1, "sophia", "Sophia", "/api/v1/engineers/a@arbisoft.com/stats?tf=1d", "Failed", "engineer-stats", DateTime.UtcNow),
            "{\"emails\":[\"a@arbisoft.com\"]}",
            [new RequestAttemptDto(1, "Failed", "Callback returned 502 BadGateway.", "<script>alert(1)</script>", "host/RequestProcessingJob", DateTime.UtcNow)]));
        await SignInAsync();

        var html = await _http.GetStringAsync("/RequestLogs/Details/1");

        Assert.Contains("&quot;emails&quot;", html);
        Assert.Contains("Callback returned 502 BadGateway.", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    private static readonly DateTime When = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListPages_RenderTheirRows()
    {
        _clients.ListAsync(Arg.Any<CancellationToken>()).Returns([new ClientDto(1, "sophia-stage", "Sophia (Stage)", true, 3600, When, "admin", null, null)]);
        _scopes.ListAsync(Arg.Any<CancellationToken>()).Returns([new ScopeDto(1, "stats:read", "Read engineer stats", When)]);
        _signingKeys.ListAsync(Arg.Any<CancellationToken>()).Returns([new SigningKeyDto(1, "key-20261110", "RS256", "signing-key-1", When, null, When, "Active")]);
        _accessTokens.ListRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            [new AccessTokenDto("jti-9", "sophia-stage", "Sophia (Stage)", "stats:read", When, When.AddHours(1), null, null, "10.0.0.1", "Active")]);
        _emailMappings.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([new EmailMappingDto(1, "abc@arbisoft.com", "abc@gmail.com", When)]);
        _requestLogs.SearchAsync(default!).ReturnsForAnyArgs(new RequestLogPage(
            [new RequestLogDto(1, "corr-77", 1, "sophia-stage", "Sophia (Stage)", "/u", "Success", "engineer-stats", When)], 1, 1, 50));
        await SignInAsync();

        foreach (var (path, expected) in new[]
        {
            ("/Clients", "sophia-stage"), ("/Scopes", "stats:read"), ("/SigningKeys", "key-20261110"),
            ("/AccessTokens", "jti-9"), ("/EmailMappings", "abc@gmail.com"), ("/RequestLogs", "corr-77"), ("/", "Integration Service Admin"),
        })
        {
            using var response = await _http.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(expected, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
    }

    [Theory]
    [InlineData("/Clients/Details/99")]
    [InlineData("/Clients/Edit/99")]
    [InlineData("/Clients/99/Secrets/Create")]
    [InlineData("/Scopes/Edit/99")]
    [InlineData("/EmailMappings/Edit/99")]
    [InlineData("/ClientConfigs/Edit/99")]
    [InlineData("/ClientConfigs/99/OutboundUrls")]
    [InlineData("/ClientConfigs/1/OutboundUrls/99/Edit")]
    [InlineData("/RequestLogs/Details/99")]
    [InlineData("/AccessTokens/Revoke/missing")]
    public async Task UnknownRecords_Return404(string path)
    {
        _clients.GetAsync(99, Arg.Any<CancellationToken>()).Returns((ClientDetailsDto?)null);
        _scopes.GetAsync(99, Arg.Any<CancellationToken>()).Returns((ScopeDto?)null);
        _emailMappings.GetAsync(99, Arg.Any<CancellationToken>()).Returns((EmailMappingDto?)null);
        _clientConfigs.ListOutboundUrlsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _requestLogs.GetAsync(99, Arg.Any<CancellationToken>()).Returns((RequestLogDetailsDto?)null);
        _accessTokens.GetAsync("missing", Arg.Any<CancellationToken>()).Returns((AccessTokenDto?)null);
        await SignInAsync();

        using var response = await _http.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditScope_LoadsTheFormAndSaves()
    {
        _scopes.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ScopeDto(1, "stats:read", "Old text", When));
        _scopes.UpdateAsync(default, default!, default!).ReturnsForAnyArgs(AdminResult.Ok);
        await SignInAsync();

        Assert.Contains("Old text", await _http.GetStringAsync("/Scopes/Edit/1"));
        using var response = await PostFormAsync("/Scopes/Edit/1", ("Name", "stats:read"), ("Description", "New text"));

        Assert.Equal("/Scopes", response.Headers.Location!.OriginalString);
        await _scopes.Received(1).UpdateAsync(1, "stats:read", "New text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateScope_ShowsTheServiceError()
    {
        _scopes.CreateAsync(default!, default!).ReturnsForAnyArgs(AdminResult<long>.Fail("A scope named 'stats:read' already exists."));
        await SignInAsync();

        using var response = await PostFormAsync("/Scopes/Create", ("Name", "stats:read"), ("Description", "x"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task EditEmailMapping_LoadsTheFormAndSaves()
    {
        _emailMappings.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new EmailMappingDto(1, "abc@arbisoft.com", "abc@gmail.com", When));
        _emailMappings.UpdateAsync(default, default!, default!).ReturnsForAnyArgs(AdminResult.Ok);
        await SignInAsync();

        Assert.Contains("abc@gmail.com", await _http.GetStringAsync("/EmailMappings/Edit/1"));
        using var response = await PostFormAsync("/EmailMappings/Edit/1", ("ArbisoftEmail", "abc@arbisoft.com"), ("Email", "abc@outlook.com"));

        Assert.Equal("/EmailMappings", response.Headers.Location!.OriginalString);
        await _emailMappings.Received(1).UpdateAsync(1, "abc@arbisoft.com", "abc@outlook.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboundUrls_ListAndEdit()
    {
        _clients.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientDetailsDto(
            new ClientDto(1, "sophia", "Sophia", true, 3600, When, "admin", null, null), [], [], []));
        _clientConfigs.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientConfigDto(
            5, 1, "oauth2", "https://sophia.test/", "/oauth2/token/", "devlake", true, null, false, null, When, 3, true, When, "admin", null, null));
        _clientConfigs.ListOutboundUrlsAsync(1, Arg.Any<CancellationToken>()).Returns(
            [new ClientConfigOutboundUrlDto(9, 5, "engineer-stats", "POST", "/api/v1/devlake/callback/", true, When, "admin", null, null)]);
        _clientConfigs.UpdateOutboundUrlAsync(default, default, default!, default!).ReturnsForAnyArgs(AdminResult.Fail("There is already an active URL for 'engineer-stats'. Deactivate it first."));
        await SignInAsync();

        Assert.Contains("/api/v1/devlake/callback/", await _http.GetStringAsync("/ClientConfigs/1/OutboundUrls"));
        Assert.Contains("/api/v1/devlake/callback/", await _http.GetStringAsync("/ClientConfigs/1/OutboundUrls/9/Edit"));
        using var response = await PostFormAsync("/ClientConfigs/1/OutboundUrls/9/Edit",
            ("Form.EventType", "engineer-stats"), ("Form.Method", "PUT"), ("Form.Path", "/cb"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already an active URL", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await _clientConfigs.Received(1).UpdateOutboundUrlAsync(1, 9, new OutboundUrlInput("engineer-stats", "PUT", "/cb", true), "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeSecret_ConfirmsThenRevokes()
    {
        _clients.RevokeSecretAsync(1, 5, Arg.Any<CancellationToken>()).Returns(AdminResult.Ok);
        await SignInAsync();

        Assert.Contains("Revoke secret 5", await _http.GetStringAsync("/Clients/1/Secrets/5/Revoke"));
        await _clients.DidNotReceiveWithAnyArgs().RevokeSecretAsync(default, default, default);
        using var response = await PostFormAsync("/Clients/1/Secrets/5/Revoke");

        Assert.Equal("/Clients/Details/1", response.Headers.Location!.OriginalString);
        await _clients.Received(1).RevokeSecretAsync(1, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeToken_ShowsTheServiceError()
    {
        _accessTokens.GetAsync("jti-1", Arg.Any<CancellationToken>()).Returns(
            new AccessTokenDto("jti-1", "sophia", "Sophia", "stats:read", When, When.AddHours(1), null, null, "::1", "Active"));
        _accessTokens.RevokeAsync(default!, default!, default!).ReturnsForAnyArgs(AdminResult.Fail("A reason of up to 900 characters is required."));
        await SignInAsync();

        using var response = await PostFormAsync("/AccessTokens/Revoke/jti-1", ("Reason", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A reason of up to 900 characters is required.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ClientDetails_ShowsTheOutboundConfigAndScopes()
    {
        _clients.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientDetailsDto(
            new ClientDto(1, "sophia", "Sophia", true, 3600, When, "admin", When, "admin2"),
            [new ClientSecretDto(4, When.AddDays(30), null, When, When, "admin", "Active")],
            [new ScopeDto(1, "stats:read", "d", When)],
            [new ScopeDto(1, "stats:read", "d", When), new ScopeDto(2, "projects:fetch", "d", When)]));
        _clientConfigs.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientConfigDto(
            5, 1, "api_token", "https://other.test/", null, null, false, null, true, null, When, 3, true, When, "admin", null, null));
        _clientConfigs.ListOutboundUrlsAsync(1, Arg.Any<CancellationToken>()).Returns(
            [new ClientConfigOutboundUrlDto(9, 5, "engineer-stats", "POST", "/cb", true, When, "admin", null, null)]);
        await SignInAsync();

        var html = WebUtility.HtmlDecode(await _http.GetStringAsync("/Clients/Details/1"));

        Assert.Contains("Static API token", html);
        Assert.Contains("1 configured", html);
        Assert.Contains("projects:fetch", html);
        Assert.Contains("by admin2", html);
    }

    [Fact]
    public async Task Logout_GetOnlyRedirectsHome()
    {
        await SignInAsync();

        using var response = await _http.GetAsync("/Account/Logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/Clients")).StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        await SignInAsync();

        // The sign-out button is in the layout of every signed-in page.
        using var signOut = await PostFormFromAsync("/Clients", "/Account/Logout");
        Assert.Equal("/Account/Login", signOut.Headers.Location!.OriginalString);

        using var page = await _http.GetAsync("/Clients");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}

public sealed class AdminPortalUnconfiguredTests
{
    [Fact]
    public async Task SignInIsRefusedWhenNoAdminIsConfigured()
    {
        using var factory = new WebApplicationFactory<WebAssemblyMarker>();
        using var http = factory.CreateClient();

        var html = await http.GetStringAsync("/Account/Login");

        Assert.Contains("Admin sign-in is not configured", html);
    }
}
