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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess;

public class DependencyInjectionTests
{
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["Database:ConnectionString"] = "Server=localhost;Database=integration;User=u;Password=p",
        ["Database:RunMigrationsOnStartup"] = "true",
        ["RabbitMq:HostName"] = "localhost",
        ["RabbitMq:UserName"] = "user",
        ["RabbitMq:Password"] = "pass",
        ["DevLake:BaseUrl"] = "http://localhost:8080",
        ["DevLake:ApiToken"] = "token",
        ["Workstream:BaseUrl"] = "http://localhost:5097",
        ["Workstream:ApiToken"] = "token"
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddDataAccess(configuration).BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddDataAccess_BindsEverySettingsSection()
    {
        using var provider = Build(ValidSettings);

        Assert.True(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.RunMigrationsOnStartup);
        Assert.Equal(5672, provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value.Port);
        Assert.Equal("http://localhost:8080", provider.GetRequiredService<IOptions<DevLakeOptions>>().Value.BaseUrl);
        Assert.Equal(30, provider.GetRequiredService<IOptions<SophiaOptions>>().Value.TimeoutSeconds);
        Assert.Equal(10, provider.GetRequiredService<IOptions<WorkstreamOptions>>().Value.PageSize);
    }

    [Theory]
    [InlineData("Database:ConnectionString")]
    [InlineData("RabbitMq:UserName")]
    [InlineData("DevLake:ApiToken")]
    [InlineData("Workstream:BaseUrl")]
    public void AddDataAccess_MissingRequiredSetting_FailsValidation(string missingKey)
    {
        var settings = new Dictionary<string, string?>(ValidSettings);
        settings.Remove(missingKey);
        using var provider = Build(settings);

        Assert.Throws<OptionsValidationException>(() => ValidateAll(provider));
    }

    private static void ValidateAll(IServiceProvider provider)
    {
        _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<DevLakeOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<WorkstreamOptions>>().Value;
    }
}
