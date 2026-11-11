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
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>
/// Creates a throwaway database on the MySQL server named by <c>INTEGRATION_TEST_MYSQL</c>, applies the migrations,
/// and drops the database afterwards. Without the variable it does nothing and the tests are skipped.
/// </summary>
public sealed class MySqlDatabaseFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "INTEGRATION_TEST_MYSQL";

    private string? _databaseName;

    public static string? ServerConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

    public string DatabaseConnectionString { get; private set; } = string.Empty;

    public ServiceProvider? Services { get; private set; }

    public async Task InitializeAsync()
    {
        var server = ServerConnectionString;
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        _databaseName = $"it_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync(server, $"CREATE DATABASE `{_databaseName}`");
        DatabaseConnectionString = $"{server};Database={_databaseName}";

        var result = DatabaseMigrationService.BuildEngine(DatabaseConnectionString, new DbUpLogAdapter(NullLogger.Instance)).PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Test database migration failed.", result.Error);
        }

        Services = BuildServices(DatabaseConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }

        var server = ServerConnectionString;
        if (_databaseName is not null && !string.IsNullOrWhiteSpace(server))
        {
            await ExecuteOnServerAsync(server, $"DROP DATABASE IF EXISTS `{_databaseName}`");
        }
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connectionString,
            ["RabbitMq:HostName"] = "localhost",
            ["RabbitMq:UserName"] = "user",
            ["RabbitMq:Password"] = "pass",
            ["DevLake:BaseUrl"] = "http://localhost:8080",
            ["DevLake:ApiToken"] = "token",
            ["Workstream:BaseUrl"] = "http://localhost:5097",
            ["Workstream:ApiToken"] = "token"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddDataAccess(configuration).BuildServiceProvider(validateScopes: true);
    }

    private static async Task ExecuteOnServerAsync(string serverConnectionString, string sql)
    {
        await using var connection = new MySqlConnection(serverConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
