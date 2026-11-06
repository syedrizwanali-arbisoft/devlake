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

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

public class MigrationScriptTests
{
    [Fact]
    public void InitialSchemaIsEmbeddedInTheDataAccessAssembly()
    {
        var names = typeof(DatabaseMigrationService).Assembly.GetManifestResourceNames();

        Assert.Contains(names, name => name.EndsWith(".Persistence.Migrations.001_InitialSchema.sql", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyRepositoryConnectionsForceUtcBecauseDbUpWritesALocalTimestamp()
    {
        var factory = new MySqlConnectionFactory("Server=localhost;Database=integration");

        Assert.Contains("DateTime Kind=Utc", factory.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kind", factory.MigrationConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartDoesNothingWhenMigrationsAreOff()
    {
        // No connection string is set, so touching the database would throw.
        var service = new DatabaseMigrationService(
            new MySqlConnectionFactory(null),
            Options.Create(new DatabaseOptions { RunMigrationsOnStartup = false }),
            NullLogger<DatabaseMigrationService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartFailsWhenMigrationsAreOnButTheConnectionStringIsMissing()
    {
        var service = new DatabaseMigrationService(
            new MySqlConnectionFactory(null),
            Options.Create(new DatabaseOptions { RunMigrationsOnStartup = true }),
            NullLogger<DatabaseMigrationService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
        Assert.Contains("ConnectionStrings__IntegrationDb", exception.Message);
    }
}
