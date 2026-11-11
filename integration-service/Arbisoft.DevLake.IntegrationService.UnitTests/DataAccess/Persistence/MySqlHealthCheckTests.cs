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

using System.Data.Common;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

public class MySqlHealthCheckTests
{
    private readonly IDbConnectionFactory _factory = Substitute.For<IDbConnectionFactory>();

    [Fact]
    public async Task WhenTheDatabaseCannotBeReached_ReportsUnhealthy_WithoutLeakingTheDetailInTheDescription()
    {
        var failure = new InvalidOperationException("Access denied for user 'root'@'10.0.0.5' using password: YES");
        _factory.OpenConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<DbConnection>(failure));

        var result = await new MySqlHealthCheck(_factory).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("The database is not reachable.", result.Description);
        Assert.DoesNotContain("root", result.Description, StringComparison.Ordinal);
        Assert.Same(failure, result.Exception);
    }

    [Fact]
    public async Task ACancelledCheck_IsNotReportedAsUnhealthy()
    {
        _factory.OpenConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<DbConnection>(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => new MySqlHealthCheck(_factory).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None));
    }
}
