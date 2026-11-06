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

using DbUp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

/// <summary>Applies pending SQL scripts at startup. A failed script throws, which stops startup.</summary>
public sealed class DatabaseMigrationService(
    MySqlConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrationService> logger) : IHostedService
{
    private const string ScriptNamespace = ".Persistence.Migrations.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.RunMigrationsOnStartup)
        {
            return Task.CompletedTask;
        }

        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionFactory.MigrationConnectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrationService).Assembly, name => name.Contains(ScriptNamespace))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database migration failed on script '{result.ErrorScript?.Name}'.", result.Error);
        }

        logger.LogInformation("Database migrations applied: {ScriptCount} new script(s).", result.Scripts.Count());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
