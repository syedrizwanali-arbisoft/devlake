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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

[Collection(MySqlTestGroup.Name)]
public class ScopeAndMappingRepositoryTests : RepositoryTestBase
{
    public ScopeAndMappingRepositoryTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [MySqlFact]
    public async Task Scope_InsertGetUpdate_AndDuplicateNameIsRejected()
    {
        var repository = Get<IScopeRepository>();
        var name = Unique("stats:read");
        var id = await repository.InsertAsync(new Scope { Name = name, Description = "Read stats", CreatedOn = Instant }, CancellationToken.None);

        var byName = await repository.GetByNameAsync(name, CancellationToken.None);
        Assert.Equal(id, byName!.Id);
        Assert.Equal("Read stats", byName.Description);

        Assert.True(await repository.UpdateAsync(new Scope { Id = id, Name = name, Description = "Changed" }, CancellationToken.None));
        Assert.Equal("Changed", (await repository.GetByIdAsync(id, CancellationToken.None))!.Description);

        var duplicate = await Assert.ThrowsAsync<MySqlException>(() =>
            repository.InsertAsync(new Scope { Name = name, CreatedOn = Instant }, CancellationToken.None));
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, duplicate.ErrorCode);
    }

    [MySqlFact]
    public async Task EmailMapping_GetMappedEmails_ReturnsOnlyThatEngineersOtherEmails()
    {
        var repository = Get<IEmailMappingRepository>();
        var owner = $"{Unique("owner")}@arbisoft.com";
        var other = $"{Unique("other")}@arbisoft.com";
        await repository.InsertAsync(new EmailMapping { ArbisoftEmail = owner, Email = "b@users.noreply.github.com", CreatedOn = Instant }, CancellationToken.None);
        await repository.InsertAsync(new EmailMapping { ArbisoftEmail = owner, Email = "a@gmail.com", CreatedOn = Instant }, CancellationToken.None);
        await repository.InsertAsync(new EmailMapping { ArbisoftEmail = other, Email = "c@gmail.com", CreatedOn = Instant }, CancellationToken.None);

        var emails = await repository.GetMappedEmailsAsync(owner, CancellationToken.None);

        Assert.Equal(["a@gmail.com", "b@users.noreply.github.com"], emails);
    }

    [MySqlFact]
    public async Task EmailMapping_LookupIgnoresCase_AndDuplicatePairIsRejected()
    {
        var repository = Get<IEmailMappingRepository>();
        var owner = $"{Unique("case")}@arbisoft.com";
        var id = await repository.InsertAsync(new EmailMapping { ArbisoftEmail = owner, Email = "x@gmail.com", CreatedOn = Instant }, CancellationToken.None);

        Assert.Single(await repository.GetMappedEmailsAsync(owner.ToUpperInvariant(), CancellationToken.None));
        Assert.True(await repository.UpdateAsync(new EmailMapping { Id = id, ArbisoftEmail = owner, Email = "y@gmail.com" }, CancellationToken.None));

        var duplicate = await Assert.ThrowsAsync<MySqlException>(() =>
            repository.InsertAsync(new EmailMapping { ArbisoftEmail = owner, Email = "y@gmail.com", CreatedOn = Instant }, CancellationToken.None));
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, duplicate.ErrorCode);
    }

    [MySqlFact]
    public async Task EmailMapping_List_ClampsPageSizeToTheMaximum()
    {
        var page = await Get<IEmailMappingRepository>().ListAsync(0, 100000, CancellationToken.None);

        Assert.Equal(1, page.Page);
        Assert.Equal(RepositoryBase.MaxPageSize, page.PageSize);
    }
}
