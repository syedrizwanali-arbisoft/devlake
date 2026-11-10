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
using Arbisoft.DevLake.IntegrationService.DataAccess;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

/// <summary>Checks the clients as registered in DI, with the standard resilience handler in front of a fake network.</summary>
public sealed class ExternalApiResilienceTests : IDisposable
{
    private readonly FakeHttpMessageHandler _network = new();
    private readonly ServiceProvider _provider;

    public ExternalApiResilienceTests()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DevLake:BaseUrl"] = "http://devlake.test",
            ["DevLake:ApiToken"] = "dl-token",
        }).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddDataAccess(configuration);
        foreach (var name in new[] { typeof(IDevLakeClient).Name, SophiaClient.TokenHttpClient, SophiaClient.CallbackHttpClient })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => _network);
            services.Configure<HttpStandardResilienceOptions>(name + "-standard", options =>
                options.Retry.Delay = TimeSpan.FromMilliseconds(1));
        }

        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task DevLake_RetriesTransientFailures()
    {
        _network.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK, "true");

        Assert.True(await _provider.GetRequiredService<IDevLakeClient>().IsTokenValidAsync());
        Assert.Equal(3, _network.Requests.Count);
        Assert.Equal("http://devlake.test/is_valid", _network.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task DevLake_DoesNotRetryAClientError()
    {
        _network.Respond(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<ExternalApiException>(() =>
            _provider.GetRequiredService<IDevLakeClient>().GetStatsAsync("a@arbisoft.com", DevLakeStatsCategory.Jira, "1d", "corr"));
        Assert.Single(_network.Requests);
    }

    [Fact]
    public async Task Sophia_RetriesTheTokenRequestButSendsTheCallbackOnce()
    {
        _network.Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, "{\"access_token\":\"tok\",\"expires_in\":3600}")
            .Respond(HttpStatusCode.ServiceUnavailable);
        var connection = new SophiaConnection(1, "https://sophia.test/", new OAuth2ClientCredentials("oauth2/token/", "id", "secret", null));

        var result = await _provider.GetRequiredService<ISophiaClient>().SendCallbackAsync(connection, "POST", "cb", "{}", "corr");

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(["/oauth2/token/", "/oauth2/token/", "/cb"], _network.Requests.Select(r => r.Uri.AbsolutePath));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("10")]
    [InlineData("120")]
    public void ClientsResolveWithAnyTimeout(string seconds)
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddDataAccess(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevLake:BaseUrl"] = "http://devlake.test", ["DevLake:TimeoutSeconds"] = seconds,
                ["Workstream:BaseUrl"] = "http://ws.test", ["Workstream:TimeoutSeconds"] = seconds,
                ["Sophia:TimeoutSeconds"] = seconds,
            }).Build())
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDevLakeClient>());
        Assert.NotNull(provider.GetRequiredService<Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream.IWorkstreamClient>());
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        Assert.NotNull(factory.CreateClient(SophiaClient.TokenHttpClient));
        Assert.NotNull(factory.CreateClient(SophiaClient.CallbackHttpClient));
    }

    [Fact]
    public async Task WorkstreamResolvesWithoutABaseUrlAndReportsItOnUse()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddDataAccess(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var client = provider.GetRequiredService<Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream.IWorkstreamClient>();

        var exception = await Assert.ThrowsAsync<ExternalApiException>(() => client.GetAllProjectsAsync());
        Assert.Contains("Workstream:BaseUrl", exception.Message);
    }

    [Fact]
    public void ResolvingAClientWithoutABaseUrlExplainsTheSetting()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddDataAccess(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDevLakeClient>());
        Assert.Contains("DevLake:BaseUrl", exception.Message);
    }
}
