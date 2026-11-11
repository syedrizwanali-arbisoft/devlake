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

using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

public class DatabaseMigrationServiceTests
{
    private static DatabaseMigrationService Create(bool runOnStartup, string connectionString)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString, RunMigrationsOnStartup = runOnStartup });
        return new DatabaseMigrationService(options, NullLogger<DatabaseMigrationService>.Instance);
    }

    [Fact]
    public async Task StartAsync_WhenTurnedOff_DoesNotTouchTheDatabase()
    {
        var service = Create(runOnStartup: false, connectionString: "Server=127.0.0.1;Port=1;Database=x;Connection Timeout=1");

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenTheMigrationFails_StopsStartup()
    {
        var service = Create(runOnStartup: true, connectionString: "Server=127.0.0.1;Port=1;Database=x;Connection Timeout=1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
    }
}
