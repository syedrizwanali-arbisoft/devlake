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

using System.Reflection;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

/// <summary>
/// Applies the SQL scripts embedded in this assembly, in filename order, when <c>Database:RunMigrationsOnStartup</c> is on.
/// DbUp records each script in its journal table, so a script runs once. A failed script stops startup.
/// </summary>
public sealed class DatabaseMigrationService : IHostedService
{
    private const string ScriptNamespace = ".Persistence.Migrations.";

    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(IOptions<DatabaseOptions> options, ILogger<DatabaseMigrationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.RunMigrationsOnStartup)
        {
            _logger.LogInformation("Database migrations are turned off for this host");
            return Task.CompletedTask;
        }

        return Task.Run(Migrate, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Migrate()
    {
        var engine = BuildEngine(_options.ConnectionString, new DbUpLogAdapter(_logger));
        var result = engine.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        _logger.LogInformation("Database is up to date");
    }

    /// <summary>Builds the DbUp engine for the scripts embedded in the DataAccess assembly.</summary>
    public static UpgradeEngine BuildEngine(string connectionString, IUpgradeLog log)
    {
        return DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.Contains(ScriptNamespace, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .LogTo(log)
            .Build();
    }
}
