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

using Arbisoft.DevLake.IntegrationService.API.Controllers;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Hosts the real API in-process against the test database. Settings the API needs are supplied here, as they would be by the environment.</summary>
public sealed class ApiFactory : WebApplicationFactory<AuthController>
{
    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string> _extraSettings;
    private readonly FakeHttpMessageHandler? _upstream;

    public ApiFactory(string connectionString, IReadOnlyDictionary<string, string>? extraSettings = null, FakeHttpMessageHandler? upstream = null)
    {
        _connectionString = connectionString;
        _extraSettings = extraSettings ?? new Dictionary<string, string>();
        _upstream = upstream;
    }

    /// <summary>The request queue this API instance uses. It is unique to the instance and deleted when the instance is disposed.</summary>
    public string QueueName { get; } = $"it.api.{Guid.NewGuid():N}";

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            RabbitMqHarness.DeleteTopologyAsync(QueueName).GetAwaiter().GetResult();
        }
    }

    /// <summary>Points the API at the test broker when one is configured; otherwise at placeholders, and readiness reports it unreachable.</summary>
    private void UseRabbitMqSettings(IWebHostBuilder builder)
    {
        var broker = RabbitMqHarness.Settings;
        builder.UseSetting("RabbitMq:HostName", broker?["host"] ?? "localhost");
        builder.UseSetting("RabbitMq:Port", broker?["port"] ?? "5672");
        builder.UseSetting("RabbitMq:UserName", broker?["user"] ?? "user");
        builder.UseSetting("RabbitMq:Password", broker?["password"] ?? "pass");
        builder.UseSetting("RabbitMq:RequestQueueName", QueueName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString", _connectionString);
        builder.UseSetting("Security:EncryptionKey", SecurityTestData.NewEncryptionKey());
        builder.UseSetting("Security:Issuer", SecurityTestData.Issuer);
        builder.UseSetting("Security:Audience", SecurityTestData.Audience);
        UseRabbitMqSettings(builder);
        builder.UseSetting("DevLake:ApiToken", "token");
        builder.UseSetting("Workstream:ApiToken", "token");

        // The job would take requests off the queue that tests are about to inspect, so it is off unless a test turns it on.
        builder.UseSetting("Processing:Enabled", "false");
        builder.ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(ProtectedTestController).Assembly));
        if (_upstream is not null)
        {
            builder.ConfigureServices(services =>
            {
                foreach (var name in new[] { ExternalHttpClients.DevLake, ExternalHttpClients.Workstream, ExternalHttpClients.SophiaToken, ExternalHttpClients.SophiaCallback })
                {
                    services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => _upstream.AsHandler());
                }
            });
        }

        foreach (var (key, value) in _extraSettings)
        {
            builder.UseSetting(key, value);
        }
    }
}
