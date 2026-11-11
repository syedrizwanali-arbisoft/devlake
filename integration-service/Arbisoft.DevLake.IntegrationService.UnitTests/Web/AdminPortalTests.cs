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
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
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
        foreach (var path in new[] { "/", "/Clients", "/Scopes", "/SigningKeys", "/AccessTokens", "/Clients/Create", "/EmailMappings", "/ClientConfigs", "/RequestLogs" })
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

    private async Task<Client> CreateClientAsync()
    {
        var clientId = Unique("cfg.");
        await PostFormAsync(Http, "/Clients/Create", ("Form.ClientId", clientId), ("Form.Name", "Config test"), ("Form.AccessTokenLifetimeSeconds", "900"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        await using var scope = _factory!.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IClientRepository>().GetByClientIdAsync(clientId, CancellationToken.None))!;
    }

    [MySqlFact]
    public async Task AnEmailMapping_IsStoredNormalized_KeepsWhatWasTyped_AndRefusesADuplicate()
    {
        await SignInAsync();
        var owner = $"{Unique("map")}@arbisoft.com".ToLowerInvariant();

        var created = await PostFormAsync(Http, "/EmailMappings/Create", ("Form.ArbisoftEmail", owner.ToUpperInvariant()), ("Form.Email", " Jane.Doe@Gmail.COM "));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailMappingRepository>();
        var stored = Assert.Single((await repository.ListAsync(owner, 1, 10, CancellationToken.None)).Items);
        Assert.Equal(owner, stored.ArbisoftEmail);
        Assert.Equal("jane.doe@gmail.com", stored.Email);
        Assert.Equal(" Jane.Doe@Gmail.COM ", stored.EmailAsReceived);
        Assert.Equal(["jane.doe@gmail.com"], await repository.GetMappedEmailsAsync(owner, CancellationToken.None));

        var duplicate = await PostFormAsync(Http, "/EmailMappings/Create", ("Form.ArbisoftEmail", owner), ("Form.Email", "JANE.DOE@gmail.com"));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var wrongDomain = await PostFormAsync(Http, "/EmailMappings/Create", ("Form.ArbisoftEmail", "someone@gmail.com"), ("Form.Email", "x@gmail.com"));
        Assert.Contains("Arbisoft", await wrongDomain.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var edited = await PostFormAsync(Http, $"/EmailMappings/Edit/{stored.Id}", ("Form.ArbisoftEmail", owner), ("Form.Email", "other@gmail.com"));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal(["other@gmail.com"], await repository.GetMappedEmailsAsync(owner, CancellationToken.None));

        Assert.Contains(owner, await Http.GetStringAsync(new Uri($"/EmailMappings?Search={Uri.EscapeDataString(owner[..10].ToUpperInvariant())}", UriKind.Relative)), StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task AnOutboundConfig_StoresItsSecretEncrypted_NeverShowsItBack_AndKeepsItWhenLeftBlank()
    {
        await SignInAsync();
        var client = await CreateClientAsync();
        var path = $"/ClientConfigs/Edit/{client.Id}";
        const string secret = "super-secret-value-123";

        var saved = await PostFormAsync(Http, path, ("Form.AuthType", "OAuth2ClientCredentials"), ("Form.BaseUrl", "https://sophia.example.com"), ("Form.TokenUrl", "https://sophia.example.com/oauth2/token/"),
            ("Form.OutboundClientId", "devlake"), ("Form.OutboundClientSecret", secret), ("Form.OutboundScope", "callback"), ("Form.MaxAttempts", "4"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var configs = scope.ServiceProvider.GetRequiredService<IClientConfigRepository>();
        var config = (await configs.GetByClientRefIdAsync(client.Id, CancellationToken.None))!;
        Assert.Equal(PortalFactory.AdminUsername, config.CreatedBy);
        Assert.Equal(4, config.MaxAttempts);
        Assert.NotNull(config.TokenRotatedOn);
        Assert.NotEqual(secret, config.OutboundClientSecretEncrypted);
        Assert.Equal(secret, scope.ServiceProvider.GetRequiredService<ISecretEncryptor>().Decrypt(config.OutboundClientSecretEncrypted!));

        foreach (var shown in new[] { path, $"/ClientConfigs/Details/{client.Id}", "/ClientConfigs" })
        {
            Assert.DoesNotContain(secret, await Http.GetStringAsync(new Uri(shown, UriKind.Relative)), StringComparison.Ordinal);
        }

        var kept = await PostFormAsync(Http, path, ("Form.AuthType", "OAuth2ClientCredentials"), ("Form.BaseUrl", "https://sophia.example.com"), ("Form.TokenUrl", "https://sophia.example.com/oauth2/token/"),
            ("Form.OutboundClientId", "devlake"), ("Form.OutboundClientSecret", string.Empty), ("Form.MaxAttempts", "5"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        Assert.Equal(HttpStatusCode.Redirect, kept.StatusCode);
        var after = (await configs.GetByClientRefIdAsync(client.Id, CancellationToken.None))!;
        Assert.Equal(config.OutboundClientSecretEncrypted, after.OutboundClientSecretEncrypted);
        Assert.Equal(5, after.MaxAttempts);
        Assert.Equal(PortalFactory.AdminUsername, after.ModifiedBy);

        var insecure = await PostFormAsync(Http, path, ("Form.AuthType", "OAuth2ClientCredentials"), ("Form.BaseUrl", "http://sophia.example.com"), ("Form.TokenUrl", "https://x.example.com/t"),
            ("Form.OutboundClientId", "devlake"), ("Form.MaxAttempts", "3"), ("Form.IsActive", "true"));
        Assert.Equal(HttpStatusCode.OK, insecure.StatusCode);
        Assert.Contains("HTTPS", await insecure.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [MySqlFact]
    public async Task CallbackUrls_AllowOnlyOneActiveUrlPerEventType()
    {
        await SignInAsync();
        var client = await CreateClientAsync();
        await PostFormAsync(Http, $"/ClientConfigs/Edit/{client.Id}", ("Form.AuthType", "ApiToken"), ("Form.BaseUrl", "https://sophia.example.com"), ("Form.ApiToken", "tok"), ("Form.MaxAttempts", "3"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        var createPath = $"/ClientConfigs/{client.Id}/Urls/Create";

        var first = await PostFormAsync(Http, createPath, ("Form.EventType", "engineer-stats"), ("Form.Method", "POST"), ("Form.Path", "/callback/"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var second = await PostFormAsync(Http, createPath, ("Form.EventType", "engineer-stats"), ("Form.Method", "POST"), ("Form.Path", "/other/"), ("Form.IsActive", "true"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("already active", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var offHost = await PostFormAsync(Http, createPath, ("Form.EventType", "engineer-stats-batch"), ("Form.Method", "POST"), ("Form.Path", "//evil.example.com/x"), ("Form.IsActive", "true"));
        Assert.Equal(HttpStatusCode.OK, offHost.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var configs = scope.ServiceProvider.GetRequiredService<IClientConfigRepository>();
        var config = (await configs.GetByClientRefIdAsync(client.Id, CancellationToken.None))!;
        var url = Assert.Single(await configs.GetOutboundUrlsAsync(config.Id, CancellationToken.None));
        Assert.Equal("/callback/", url.Path);
        Assert.Equal(PortalFactory.AdminUsername, url.CreatedBy);

        var edited = await PostFormAsync(Http, $"/ClientConfigs/{client.Id}/Urls/{url.Id}/Edit", ("Form.EventType", "engineer-stats"), ("Form.Method", "PUT"), ("Form.Path", "/callback/"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal("PUT", (await configs.GetOutboundUrlByIdAsync(url.Id, CancellationToken.None))!.Method);
    }

    [MySqlFact]
    public async Task RequestLogs_CanBeFiltered_AndOneShowsItsBodyAndDeliveryAttempts()
    {
        await SignInAsync();
        var client = await CreateClientAsync();
        await using var scope = _factory!.Services.CreateAsyncScope();
        var logs = scope.ServiceProvider.GetRequiredService<IRequestLogRepository>();
        var correlation = Guid.NewGuid().ToString();
        var id = await logs.InsertAsync(
            new RequestLog { ClientRefId = client.Id, CorrelationId = correlation, Url = "/api/v1/engineers/x/stats?tf=24h", Status = RequestStatus.Failed, EventType = "engineer-stats", CreatedOn = DateTimeOffset.UtcNow },
            CancellationToken.None);
        await logs.InsertJsonAsync(new RequestLogJson { RequestLogId = id, Json = "{\"email\":\"jane@arbisoft.com\",\"time_frame\":\"24h\"}" }, CancellationToken.None);
        await logs.InsertAttemptAsync(
            new RequestResponseAttempt { RequestLogId = id, ResponseJson = "{\"detail\":\"bad gateway\"}", AttemptStatus = "Failed", FailureError = "HTTP 502", AttemptBy = "job", AttemptOn = DateTimeOffset.UtcNow },
            CancellationToken.None);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var listed = await Http.GetStringAsync(new Uri($"/RequestLogs?ClientRefId={client.Id}&Status=Failed&From={today}&To={today}", UriKind.Relative));
        Assert.Contains(correlation, listed, StringComparison.Ordinal);
        var otherStatus = await Http.GetStringAsync(new Uri($"/RequestLogs?ClientRefId={client.Id}&Status=Success", UriKind.Relative));
        Assert.DoesNotContain(correlation, otherStatus, StringComparison.Ordinal);
        var otherDay = await Http.GetStringAsync(new Uri($"/RequestLogs?ClientRefId={client.Id}&From=2000-01-01&To=2000-01-02", UriKind.Relative));
        Assert.DoesNotContain(correlation, otherDay, StringComparison.Ordinal);

        var details = await Http.GetStringAsync(new Uri($"/RequestLogs/Details/{id}", UriKind.Relative));
        Assert.Contains("jane@arbisoft.com", details, StringComparison.Ordinal);
        Assert.Contains("HTTP 502", details, StringComparison.Ordinal);
        Assert.Contains("bad gateway", details, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(new Uri("/RequestLogs/Details/999999999", UriKind.Relative))).StatusCode);
    }

    [MySqlFact]
    public async Task TheIntegrationPages_AllRenderForASignedInAdmin()
    {
        await SignInAsync();
        var client = await CreateClientAsync();
        await PostFormAsync(Http, $"/ClientConfigs/Edit/{client.Id}", ("Form.AuthType", "ApiToken"), ("Form.BaseUrl", "https://sophia.example.com"), ("Form.ApiToken", "tok"), ("Form.MaxAttempts", "3"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        await PostFormAsync(Http, $"/ClientConfigs/{client.Id}/Urls/Create", ("Form.EventType", "engineer-stats"), ("Form.Method", "POST"), ("Form.Path", "/cb/"), ("Form.IsActive", "true"), ("Form.IsActive", "false"));
        var owner = $"{Unique("pg")}@arbisoft.com".ToLowerInvariant();
        await PostFormAsync(Http, "/EmailMappings/Create", ("Form.ArbisoftEmail", owner), ("Form.Email", "x@gmail.com"));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var mapping = Assert.Single((await scope.ServiceProvider.GetRequiredService<IEmailMappingRepository>().ListAsync(owner, 1, 10, CancellationToken.None)).Items);
        var configs = scope.ServiceProvider.GetRequiredService<IClientConfigRepository>();
        var url = Assert.Single(await configs.GetOutboundUrlsAsync((await configs.GetByClientRefIdAsync(client.Id, CancellationToken.None))!.Id, CancellationToken.None));

        var paths = new[]
        {
            "/EmailMappings", "/EmailMappings/Create", $"/EmailMappings/Edit/{mapping.Id}", "/ClientConfigs", $"/ClientConfigs/Details/{client.Id}", $"/ClientConfigs/Edit/{client.Id}",
            $"/ClientConfigs/{client.Id}/Urls/Create", $"/ClientConfigs/{client.Id}/Urls/{url.Id}/Edit", "/RequestLogs"
        };
        foreach (var path in paths)
        {
            var response = await Http.GetAsync(new Uri(path, UriKind.Relative));

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}.");
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(new Uri("/ClientConfigs/Details/999999999", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(new Uri("/EmailMappings/Edit/999999999", UriKind.Relative))).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("<dd class=\"mono long\">([^<]+)</dd>")]
    private static partial Regex SecretPattern();
}
