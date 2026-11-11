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

using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>
/// Creates a throwaway database, applies the real migrations to it, and drops it afterwards. It never touches an
/// existing database.
/// </summary>
public sealed class MySqlDatabaseFixture : IAsyncLifetime
{
    private readonly string _serverConnectionString =
        Environment.GetEnvironmentVariable(MySqlFactAttribute.EnvironmentVariable) ?? string.Empty;

    public string DatabaseName { get; } = "integration_test_" + Guid.NewGuid().ToString("N")[..12];

    public MySqlConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ExecuteOnServerAsync($"CREATE DATABASE `{DatabaseName}`");
        var builder = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = DatabaseName };
        ConnectionFactory = new MySqlConnectionFactory(builder.ConnectionString);
        await MigrateAsync();
    }

    /// <summary>Runs the migration service the API runs at startup.</summary>
    public Task MigrateAsync() => new DatabaseMigrationService(
        ConnectionFactory,
        Options.Create(new DatabaseOptions { RunMigrationsOnStartup = true }),
        NullLogger<DatabaseMigrationService>.Instance).StartAsync(CancellationToken.None);

    public async Task DisposeAsync() => await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS `{DatabaseName}`");

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlDatabaseFixture>
{
    public const string Name = "MySQL";
}
