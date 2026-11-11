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

using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class ScopeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IScopeRepository _scopes = Substitute.For<IScopeRepository>();
    private readonly ScopeService _service;

    public ScopeServiceTests()
    {
        _service = new ScopeService(_scopes, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Create_AValidScope_IsInserted()
    {
        _scopes.InsertAsync(Arg.Any<Scope>(), Arg.Any<CancellationToken>()).Returns(7L);

        var result = await _service.CreateAsync("reports:read", " Read reports ", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(7, result.Value);
        await _scopes.Received(1).InsertAsync(
            Arg.Is<Scope>(s => s.Name == "reports:read" && s.Description == "Read reports" && s.CreatedOn == Now), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Stats:Read")]
    [InlineData("1stats")]
    [InlineData("has space")]
    public async Task Create_AnInvalidName_IsRefusedWithoutAWrite(string name)
    {
        var result = await _service.CreateAsync(name, string.Empty, CancellationToken.None);

        Assert.False(result.Succeeded);
        await _scopes.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Create_ADescriptionThatIsTooLong_IsRefused()
    {
        var result = await _service.CreateAsync("reports:read", new string('x', ScopeService.MaxDescriptionLength + 1), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Create_ANameThatExists_IsRefused()
    {
        _scopes.GetByNameAsync("stats:read", Arg.Any<CancellationToken>()).Returns(new Scope { Id = 1, Name = "stats:read" });

        var result = await _service.CreateAsync("stats:read", string.Empty, CancellationToken.None);

        Assert.False(result.Succeeded);
        await _scopes.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Update_AnUnknownScope_Fails()
    {
        var result = await _service.UpdateAsync(9, "stats:read", string.Empty, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Update_ANameUsedByAnotherScope_Fails()
    {
        _scopes.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Scope { Id = 1, Name = "a:b" });
        _scopes.GetByNameAsync("stats:read", Arg.Any<CancellationToken>()).Returns(new Scope { Id = 2, Name = "stats:read" });

        var result = await _service.UpdateAsync(1, "stats:read", string.Empty, CancellationToken.None);

        Assert.False(result.Succeeded);
        await _scopes.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Update_KeepingItsOwnName_Succeeds_AndKeepsTheCreatedTime()
    {
        var created = Now.AddDays(-3);
        _scopes.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Scope { Id = 1, Name = "stats:read", CreatedOn = created });
        _scopes.GetByNameAsync("stats:read", Arg.Any<CancellationToken>()).Returns(new Scope { Id = 1, Name = "stats:read", CreatedOn = created });

        var result = await _service.UpdateAsync(1, "stats:read", "New text", CancellationToken.None);

        Assert.True(result.Succeeded);
        await _scopes.Received(1).UpdateAsync(Arg.Is<Scope>(s => s.Id == 1 && s.Description == "New text" && s.CreatedOn == created), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_ReturnsTheScopes()
    {
        _scopes.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Scope>([new Scope { Id = 1, Name = "stats:read", Description = "d", CreatedOn = Now }], 1, 1, 200));

        var list = await _service.ListAsync(CancellationToken.None);

        var scope = Assert.Single(list);
        Assert.Equal("stats:read", scope.Name);
    }

    [Fact]
    public async Task Get_AnUnknownScope_IsNull()
    {
        Assert.Null(await _service.GetAsync(4, CancellationToken.None));
    }
}
