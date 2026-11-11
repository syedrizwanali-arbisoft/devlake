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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

[Collection(MySqlTestGroup.Name)]
public class MigrationTests
{
    private static readonly string[] ExpectedTables =
    [
        "EmailMappings", "Clients", "ClientSecrets", "ClientScopes", "Scopes", "SigningKeys", "AccessTokens",
        "RequestLogs", "RequestLogJsons", "RequestResponseAttempts", "ClientConfigs", "ClientConfigOutboundUrls", "SecretStoreEntries"
    ];

    private readonly MySqlDatabaseFixture _fixture;

    public MigrationTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [MySqlFact]
    public async Task InitialSchema_CreatesEveryTable()
    {
        await using var connection = new MySqlConnection(_fixture.DatabaseConnectionString);

        var tables = (await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()")).ToList();

        Assert.All(ExpectedTables, table => Assert.Contains(table, tables));
    }

    [MySqlFact]
    public async Task SeedMigration_CreatesTheTwoScopesTheApisRequire()
    {
        await using var connection = new MySqlConnection(_fixture.DatabaseConnectionString);

        var names = (await connection.QueryAsync<string>("SELECT name FROM Scopes WHERE name IN ('stats:read', 'projects:fetch')")).ToList();

        Assert.Equal(2, names.Count);
    }

    [MySqlFact]
    public void RunningAgain_AppliesNothing()
    {
        var engine = DatabaseMigrationService.BuildEngine(_fixture.DatabaseConnectionString, new DbUpLogAdapter(NullLogger.Instance));

        var result = engine.PerformUpgrade();

        Assert.True(result.Successful);
        Assert.Empty(result.Scripts);
    }

    [Fact]
    public void MigrationScripts_AreEmbeddedInTheDataAccessAssembly()
    {
        var names = typeof(DatabaseMigrationService).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(4, names.Count);
        Assert.Contains(names, name => name.EndsWith(".Persistence.Migrations.001_InitialSchema.sql", StringComparison.Ordinal));
        Assert.Contains(names, name => name.EndsWith(".Persistence.Migrations.002_SecretStore.sql", StringComparison.Ordinal));
        Assert.Contains(names, name => name.EndsWith(".Persistence.Migrations.003_SeedScopes.sql", StringComparison.Ordinal));
        Assert.Contains(names, name => name.EndsWith(".Persistence.Migrations.004_RequestIdempotency.sql", StringComparison.Ordinal));
    }
}
