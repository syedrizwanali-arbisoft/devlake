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

using System.Net;
using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Web;

/// <summary>The admin portal end to end: real routing, anti-forgery, cookie sign-in and MySQL.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed partial class AdminPortalTests : IAsyncLifetime, IDisposable
{
    private readonly MySqlDatabaseFixture _database;
    private PortalFactory? _factory;
    private HttpClient? _http;

    public AdminPortalTests(MySqlDatabaseFixture database)
    {
        _database = database;
    }

    private HttpClient Http => _http!;

    public Task InitializeAsync()
    {
        if (_database.Services is not null)
        {
            _factory = new PortalFactory(_database.DatabaseConnectionString);
            _http = NewClient(_factory);
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _http?.Dispose();
        _factory?.Dispose();
    }

    private static HttpClient NewClient(PortalFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..24];

    /// <summary>Fetches a page and returns the anti-forgery token its form carries, which pairs with the cookie the client now holds.</summary>
    private static async Task<string> TokenFromAsync(HttpClient http, string path)
    {
        var html = await http.GetStringAsync(new Uri(path, UriKind.Relative));
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"{path} has no anti-forgery token.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient http, string path, params (string Key, string Value)[] fields)
    {
        var token = await TokenFromAsync(http, path);
        var form = fields.Select(f => KeyValuePair.Create(f.Key, f.Value)).Append(KeyValuePair.Create("__RequestVerificationToken", token));
        return await http.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private async Task SignInAsync()
    {
        var response = await PostFormAsync(Http, "/Account/Login", ("Username", PortalFactory.AdminUsername), ("Password", PortalFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [MySqlFact]
    public async Task EveryPage_SendsAnAnonymousVisitorToTheSignInPage()
    {
        foreach (var path in new[] { "/", "/Clients", "/Scopes", "/SigningKeys", "/AccessTokens", "/Clients/Create" })
        {
            var response = await Http.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        }
    }

    [MySqlFact]
    public async Task TheHealthProbe_IsOpenWithoutSignIn()
    {
        var response = await Http.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [MySqlFact]
    public async Task APostWithoutTheAntiForgeryToken_IsRefused()
    {
        await SignInAsync();

        var response = await Http.PostAsync(new Uri("/Scopes/Create", UriKind.Relative), new FormUrlEncodedContent([KeyValuePair.Create("Name", "x:y")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [MySqlFact]
    public async Task SignIn_WithAWrongPassword_ShowsAnErrorAndGivesNoAccess()
    {
        var response = await PostFormAsync(Http, "/Account/Login", ("Username", PortalFactory.AdminUsername), ("Password", "wrong"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The username or password is wrong.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await Http.GetAsync(new Uri("/Clients", UriKind.Relative))).StatusCode);
    }

    [MySqlFact]
    public async Task SignIn_WithAWrongUsername_IsRefused()
    {
        var response = await PostFormAsync(Http, "/Account/Login", ("Username", "someone"), ("Password", PortalFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [MySqlFact]
    public async Task SignIn_WithNoAdminConfigured_IsAlwaysRefused()
    {
        using var factory = new PortalFactory(_database.DatabaseConnectionString, configureAdmin: false);
        using var http = NewClient(factory);

        var response = await PostFormAsync(http, "/Account/Login", ("Username", string.Empty), ("Password", string.Empty));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await http.GetAsync(new Uri("/Clients", UriKind.Relative))).StatusCode);
    }

    [MySqlFact]
    public async Task SignIn_ReturnsToALocalPage_ButNeverToAnotherSite()
    {
        var local = await PostFormAsync(Http, "/Account/Login?ReturnUrl=%2FScopes", ("Username", PortalFactory.AdminUsername), ("Password", PortalFactory.AdminPassword));
        Assert.Equal("/Scopes", local.Headers.Location!.OriginalString);

        using var other = NewClient(_factory!);
        var external = await PostFormAsync(other, "/Account/Login?ReturnUrl=https%3A%2F%2Fevil.example%2F", ("Username", PortalFactory.AdminUsername), ("Password", PortalFactory.AdminPassword));
        Assert.Equal("/", external.Headers.Location!.OriginalString);
    }

    [MySqlFact]
    public async Task SignOut_EndsTheSession()
    {
        await SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(new Uri("/Clients", UriKind.Relative))).StatusCode);

        var token = await TokenFromAsync(Http, "/Clients");
        await Http.PostAsync(new Uri("/Account/Logout", UriKind.Relative), new FormUrlEncodedContent([KeyValuePair.Create("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, (await Http.GetAsync(new Uri("/Clients", UriKind.Relative))).StatusCode);
    }

    [MySqlFact]
    public async Task ACreatedClient_ShowsASecretOnceAndKeepsOnlyItsHash()
    {
        await SignInAsync();
        var clientId = Unique("portal.");

        var created = await PostFormAsync(Http, "/Clients/Create", ("Form.ClientId", clientId), ("Form.Name", "Portal test"), ("Form.AccessTokenLifetimeSeconds", "900"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
        var client = await clients.GetByClientIdAsync(clientId, CancellationToken.None);
        Assert.NotNull(client);
        Assert.Equal(PortalFactory.AdminUsername, client.CreatedBy);
        Assert.True(client.IsActive);

        var secretPage = await PostFormAsync(Http, $"/Clients/{client.Id}/Secrets/Create", ("ValidForDays", "30"));
        var body = await secretPage.Content.ReadAsStringAsync();
        var plain = SecretPattern().Match(body);
        Assert.True(plain.Success, "The page should show the new secret.");
        Assert.Contains("no-store", secretPage.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        var details = await Http.GetStringAsync(new Uri($"/Clients/Details/{client.Id}", UriKind.Relative));
        Assert.DoesNotContain(plain.Groups[1].Value, details, StringComparison.Ordinal);

        var stored = Assert.Single(await clients.GetSecretsAsync(client.Id, CancellationToken.None));
        Assert.NotEqual(plain.Groups[1].Value, stored.Secret);
        Assert.True(new ClientSecretHasher().Verify(plain.Groups[1].Value, stored.Secret));
    }

    [MySqlFact]
    public async Task ACreatedScope_IsListed_AndADuplicateIsRefused()
    {
        await SignInAsync();
        var name = Unique("e2e:").ToLowerInvariant();

        var first = await PostFormAsync(Http, "/Scopes/Create", ("Name", name), ("Description", "from a test"));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Contains(name, await Http.GetStringAsync(new Uri("/Scopes", UriKind.Relative)), StringComparison.Ordinal);

        var second = await PostFormAsync(Http, "/Scopes/Create", ("Name", name), ("Description", string.Empty));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("already exists", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task RotatingTheSigningKey_AddsAnActiveKey_AndStoresItsPrivateHalfEncrypted()
    {
        await SignInAsync();

        var response = await PostFormAsync(Http, "/SigningKeys/Rotate");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var active = await scope.ServiceProvider.GetRequiredService<ISigningKeyRepository>().GetActiveAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.NotNull(active);
        var stored = await scope.ServiceProvider.GetRequiredService<ISecretStoreRepository>().GetEncryptedValueAsync(active.PrivateKeyRef, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.DoesNotContain("PRIVATE KEY", stored, StringComparison.Ordinal);
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        Assert.Contains("PRIVATE KEY", (await secrets.GetAsync(active.PrivateKeyRef, CancellationToken.None))!, StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task EveryPage_RendersForASignedInAdmin()
    {
        await SignInAsync();
        var clientId = Unique("render.");
        await PostFormAsync(Http, "/Clients/Create", ("Form.ClientId", clientId), ("Form.Name", "Render test"), ("Form.AccessTokenLifetimeSeconds", "900"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var client = (await scope.ServiceProvider.GetRequiredService<IClientRepository>().GetByClientIdAsync(clientId, CancellationToken.None))!;
        var statsScope = (await scope.ServiceProvider.GetRequiredService<IScopeRepository>().GetByNameAsync("stats:read", CancellationToken.None))!;
        await PostFormAsync(Http, $"/Clients/{client.Id}/Secrets/Create", ("ValidForDays", "30"));
        var secret = Assert.Single(await scope.ServiceProvider.GetRequiredService<IClientRepository>().GetSecretsAsync(client.Id, CancellationToken.None));

        var paths = new[]
        {
            "/", "/Clients", "/Clients/Create", $"/Clients/Details/{client.Id}", $"/Clients/Edit/{client.Id}", $"/Clients/{client.Id}/Secrets/Create",
            $"/Clients/{client.Id}/Secrets/{secret.Id}/Revoke", "/Scopes", "/Scopes/Create", $"/Scopes/Edit/{statsScope.Id}", "/SigningKeys", "/SigningKeys/Rotate", "/AccessTokens"
        };
        foreach (var path in paths)
        {
            var response = await Http.GetAsync(new Uri(path, UriKind.Relative));

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}.");
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(new Uri("/Clients/Details/999999999", UriKind.Relative))).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("<dd class=\"mono long\">([^<]+)</dd>")]
    private static partial Regex SecretPattern();
}
