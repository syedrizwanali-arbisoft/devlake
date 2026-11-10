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
