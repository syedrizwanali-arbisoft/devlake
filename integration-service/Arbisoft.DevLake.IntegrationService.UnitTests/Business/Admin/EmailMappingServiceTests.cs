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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class EmailMappingServiceTests
{
    private readonly IEmailMappingRepository _mappings = Substitute.For<IEmailMappingRepository>();
    private readonly EmailMappingService _service;

    public EmailMappingServiceTests()
    {
        _mappings.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new EmailMapping { Id = 1, ArbisoftEmail = "abc@arbisoft.com", Email = "abc@gmail.com" },
            new EmailMapping { Id = 2, ArbisoftEmail = "def@arbisoft.com", Email = "def.dev@outlook.com" }]);
        _mappings.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new EmailMapping { Id = 1, ArbisoftEmail = "abc@arbisoft.com", Email = "abc@gmail.com" });
        _mappings.AddAsync(Arg.Any<EmailMapping>(), Arg.Any<CancellationToken>()).Returns(3L);
        _service = new EmailMappingService(_mappings, Options.Create(new RequestOptions()), new FakeTimeProvider());
    }

    [Fact]
    public async Task Create_NormalizesBothEmails()
    {
        var result = await _service.CreateAsync(" ABC@Arbisoft.com ", " Abc.Work@GitHub.io ");

        Assert.True(result.Succeeded);
        await _mappings.Received(1).AddAsync(Arg.Is<EmailMapping>(m => m.ArbisoftEmail == "abc@arbisoft.com" && m.Email == "abc.work@github.io"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("abc@gmail.com", "x@gmail.com", "@arbisoft.com")]
    [InlineData("abc@sub.arbisoft.com", "x@gmail.com", "@arbisoft.com")]
    [InlineData("abc@arbisoft.com", "not-an-email", "valid email")]
    [InlineData("abc@arbisoft.com", "ABC@arbisoft.com", "doesn't need a mapping")]
    [InlineData("abc@arbisoft.com", "ABC@gmail.com", "already mapped")]
    public async Task Create_RejectsInvalidOrDuplicateMappings(string arbisoft, string email, string mentions)
    {
        var result = await _service.CreateAsync(arbisoft, email);

        Assert.False(result.Succeeded);
        Assert.Contains(mentions, result.Error);
        await _mappings.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Update_AllowsSavingTheSameMappingUnchanged()
    {
        Assert.True((await _service.UpdateAsync(1, "abc@arbisoft.com", "abc@gmail.com")).Succeeded);
    }

    [Theory]
    [InlineData("DEF", 1)]
    [InlineData("gmail", 1)]
    [InlineData(null, 2)]
    public async Task List_FiltersByEitherEmail(string? search, int expected)
    {
        Assert.Equal(expected, (await _service.ListAsync(search)).Count);
    }

    [Fact]
    public async Task Get_MapsTheMappingOrReturnsNull()
    {
        _mappings.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((EmailMapping?)null);

        Assert.Equal("abc@gmail.com", (await _service.GetAsync(1))!.Email);
        Assert.Null(await _service.GetAsync(99));
    }

    [Fact]
    public async Task Update_RejectsAnUnknownMappingAndInvalidEmails()
    {
        _mappings.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((EmailMapping?)null);

        Assert.Contains("not found", (await _service.UpdateAsync(99, "abc@arbisoft.com", "x@gmail.com")).Error);
        Assert.Contains("@arbisoft.com", (await _service.UpdateAsync(1, "abc@gmail.com", "x@gmail.com")).Error);
        await _mappings.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
