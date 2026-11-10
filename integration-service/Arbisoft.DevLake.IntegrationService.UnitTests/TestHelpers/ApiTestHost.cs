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

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Runs the real API pipeline in memory, with only the repositories and the queue replaced.</summary>
public sealed class ApiTestHost : IDisposable
{
    public const string ClientSecretValue = "the-client-secret";

    private readonly WebApplicationFactory<Program> _factory;

    public ApiTestHost(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var clientSecret = new ClientSecret { Id = 10, Secret = new SecretHasher().Hash(ClientSecretValue) };
        Clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        Clients.GetUsableSecretsAsync(1, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([clientSecret]);
        GrantScopes("stats:read");
        SigningKeys.GetActiveAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(FirstKey.Row);
        SigningKeys.GetAllAsync(Arg.Any<CancellationToken>()).Returns([FirstKey.Row]);
        AccessTokens.AddAsync(Arg.Do<AccessToken>(t => SavedTokens[t.Jti] = t), Arg.Any<CancellationToken>()).Returns(1L);
        AccessTokens.GetByJtiAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => SavedTokens.GetValueOrDefault(call.Arg<string>()));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Database:RunMigrationsOnStartup", "false");
            host.UseSetting("Secrets:key-1", FirstKey.PrivatePem);
            host.UseSetting("Secrets:key-2", SecondKey.PrivatePem);
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                host.UseSetting(key, value);
            }

            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(Clients));
                services.Replace(ServiceDescriptor.Singleton(Scopes));
                services.Replace(ServiceDescriptor.Singleton(AccessTokens));
                services.Replace(ServiceDescriptor.Singleton(SigningKeys));
                services.Replace(ServiceDescriptor.Singleton(RequestLogs));
                services.Replace(ServiceDescriptor.Singleton(ClientConfigs));
                services.Replace(ServiceDescriptor.Singleton(Publisher));
                services.AddControllers().AddApplicationPart(typeof(ApiTestHost).Assembly);
            });
        });
        Http = _factory.CreateClient();
    }

    public IClientRepository Clients { get; } = Substitute.For<IClientRepository>();
    public IScopeRepository Scopes { get; } = Substitute.For<IScopeRepository>();
    public IAccessTokenRepository AccessTokens { get; } = Substitute.For<IAccessTokenRepository>();
    public ISigningKeyRepository SigningKeys { get; } = Substitute.For<ISigningKeyRepository>();
    public IRequestLogRepository RequestLogs { get; } = Substitute.For<IRequestLogRepository>();
    public IClientConfigRepository ClientConfigs { get; } = Substitute.For<IClientConfigRepository>();
    public IRequestQueuePublisher Publisher { get; } = Substitute.For<IRequestQueuePublisher>();
    public Dictionary<string, AccessToken> SavedTokens { get; } = [];
    public TestKey FirstKey { get; } = TestKey.Create(1, "key-1", DateTime.UtcNow.AddDays(-30));
    public TestKey SecondKey { get; } = TestKey.Create(2, "key-2", DateTime.UtcNow.AddDays(-1));
    public HttpClient Http { get; }

    public void GrantScopes(params string[] scopes) =>
        Scopes.GetNamesForClientAsync(1, Arg.Any<CancellationToken>()).Returns(scopes);

    public async Task<HttpResponseMessage> PostTokenAsync(string credentials, string body = "grant_type=client_credentials")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        return await Http.SendAsync(request);
    }

    public async Task<string> GetTokenAsync()
    {
        using var response = await PostTokenAsync("sophia:" + ClientSecretValue);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await Http.SendAsync(request);
    }

    public void Dispose()
    {
        Http.Dispose();
        _factory.Dispose();
    }
}
