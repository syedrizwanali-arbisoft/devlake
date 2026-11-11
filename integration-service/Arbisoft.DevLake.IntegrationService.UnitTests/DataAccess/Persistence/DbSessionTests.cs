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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

[Collection(MySqlTestGroup.Name)]
public class DbSessionTests : RepositoryTestBase
{
    private readonly MySqlDatabaseFixture _fixture;

    public DbSessionTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    private static Scope NewScope() => new() { Name = Guid.NewGuid().ToString("N"), CreatedOn = Instant };

    [MySqlFact]
    public async Task Commit_KeepsTheWrites_AndDisposingWithoutCommitRollsThemBack()
    {
        long committed;
        long abandoned;
        await using (var scope = _fixture.Services!.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
            var repository = scope.ServiceProvider.GetRequiredService<IScopeRepository>();
            await session.BeginTransactionAsync(CancellationToken.None);
            committed = await repository.InsertAsync(NewScope(), CancellationToken.None);
            await session.CommitAsync(CancellationToken.None);
            await session.BeginTransactionAsync(CancellationToken.None);
            abandoned = await repository.InsertAsync(NewScope(), CancellationToken.None);
        }

        var reader = Get<IScopeRepository>();
        Assert.NotNull(await reader.GetByIdAsync(committed, CancellationToken.None));
        Assert.Null(await reader.GetByIdAsync(abandoned, CancellationToken.None));
    }

    [MySqlFact]
    public async Task BeginTransaction_WhenOneIsOpen_Throws()
    {
        var session = Get<IDbSession>();
        await session.BeginTransactionAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.BeginTransactionAsync(CancellationToken.None));
    }

    [MySqlFact]
    public async Task Commit_WithoutATransaction_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Get<IDbSession>().CommitAsync(CancellationToken.None));
    }
}
