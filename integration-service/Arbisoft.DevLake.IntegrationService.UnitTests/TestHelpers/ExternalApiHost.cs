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

using Arbisoft.DevLake.IntegrationService.DataAccess;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>
/// The real HTTP clients with their real timeout, retry and circuit-breaker pipelines, wired to a <see cref="FakeHttpMessageHandler"/> instead of
/// the network. Retry delays are one millisecond and the breaker needs many calls to open, so tests are quick and independent unless they ask otherwise.
/// </summary>
public sealed class ExternalApiHost : IDisposable
{
    public const string DevLakeBase = "https://devlake.test/rest/";
    public const string WorkstreamBase = "https://workstream.test/api/";
    public const string ApiToken = "devlake-token-value";

    private readonly ServiceProvider _provider;

    public ExternalApiHost(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Server=localhost;Database=unused",
            ["RabbitMq:HostName"] = "localhost",
            ["RabbitMq:UserName"] = "u",
            ["RabbitMq:Password"] = "p",
            ["DevLake:BaseUrl"] = DevLakeBase,
            ["DevLake:ApiToken"] = ApiToken,
            ["Workstream:BaseUrl"] = WorkstreamBase,
            ["Workstream:ApiToken"] = "workstream-token-value",
            ["Resilience:RetryBaseDelayMilliseconds"] = "1",
            ["Resilience:BreakerMinimumThroughput"] = "1000"
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(Logs)).AddDataAccess(configuration);
        services.AddScoped(_ => Configs);
        foreach (var name in new[] { ExternalHttpClients.DevLake, ExternalHttpClients.Workstream, ExternalHttpClients.SophiaToken, ExternalHttpClients.SophiaCallback })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Handler.AsHandler());
        }

        _provider = services.BuildServiceProvider();
    }

    public FakeHttpMessageHandler Handler { get; } = new();

    public CapturedLogs Logs { get; } = new();

    public IClientConfigRepository Configs { get; } = Substitute.For<IClientConfigRepository>();

    public IHttpClientFactory HttpClientFactory => _provider.GetRequiredService<IHttpClientFactory>();

    public ILogger<T> Logger<T>() => _provider.GetRequiredService<ILogger<T>>();

    public AsyncServiceScope Scope() => _provider.CreateAsyncScope();

    public void Dispose()
    {
        _provider.Dispose();
    }
}
