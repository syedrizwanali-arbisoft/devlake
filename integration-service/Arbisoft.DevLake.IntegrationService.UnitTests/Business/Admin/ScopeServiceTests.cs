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
    private readonly IScopeRepository _scopes = Substitute.For<IScopeRepository>();
    private readonly ScopeService _service;

    public ScopeServiceTests()
    {
        _scopes.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Scope { Id = 1, Name = "stats:read" }]);
        _scopes.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Scope { Id = 1, Name = "stats:read" });
        _scopes.AddAsync(Arg.Any<Scope>(), Arg.Any<CancellationToken>()).Returns(2L);
        _service = new ScopeService(_scopes, new FakeTimeProvider());
    }

    [Theory]
    [InlineData("projects:fetch")]
    [InlineData("items.read")]
    public async Task Create_AcceptsValidNames(string name)
    {
        Assert.True((await _service.CreateAsync(name, "d")).Succeeded);
    }

    [Theory]
    [InlineData("stats:read", "already exists")]
    [InlineData("Stats:Read", "lowercase")]
    [InlineData("x", "2 to 128")]
    [InlineData("has space", "lowercase")]
    [InlineData("1abc", "start with")]
    public async Task Create_RejectsInvalidOrDuplicateNames(string name, string mentions)
    {
        var result = await _service.CreateAsync(name, "d");

        Assert.False(result.Succeeded);
        Assert.Contains(mentions, result.Error);
    }

    [Fact]
    public async Task Update_AllowsKeepingTheSameName()
    {
        Assert.True((await _service.UpdateAsync(1, "stats:read", "new description")).Succeeded);
        await _scopes.Received(1).UpdateAsync(Arg.Is<Scope>(s => s.Description == "new description"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAndGet_MapTheScopes()
    {
        _scopes.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Scope?)null);

        Assert.Equal(["stats:read"], (await _service.ListAsync()).Select(s => s.Name));
        Assert.Equal("stats:read", (await _service.GetAsync(1))!.Name);
        Assert.Null(await _service.GetAsync(99));
    }

    [Fact]
    public async Task Update_RejectsUnknownScopesBadNamesAndLongDescriptions()
    {
        _scopes.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Scope?)null);

        Assert.Contains("not found", (await _service.UpdateAsync(99, "x:y", "d")).Error);
        Assert.Contains("lowercase", (await _service.UpdateAsync(1, "Bad Name", "d")).Error);
        Assert.Contains("1024", (await _service.CreateAsync("new:scope", new string('x', 1025))).Error);
        await _scopes.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
