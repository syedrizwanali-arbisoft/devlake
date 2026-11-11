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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
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
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IEmailMappingRepository _mappings = Substitute.For<IEmailMappingRepository>();
    private readonly EmailMappingService _service;

    public EmailMappingServiceTests()
    {
        _mappings.InsertAsync(Arg.Any<EmailMapping>(), Arg.Any<CancellationToken>()).Returns(5L);
        _service = new EmailMappingService(_mappings, Options.Create(new RequestsOptions()), new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Create_StoresBothAddressesNormalized_AndTheMappedOneAsTyped()
    {
        var result = await _service.CreateAsync(new EmailMappingInput(" Jane.Doe@Arbisoft.com ", " Jane.D@Gmail.com "), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Value);
        await _mappings.Received(1).InsertAsync(
            Arg.Is<EmailMapping>(m => m.ArbisoftEmail == "jane.doe@arbisoft.com" && m.Email == "jane.d@gmail.com" && m.EmailAsReceived == " Jane.D@Gmail.com " && m.CreatedOn == Now),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("jane@gmail.com", "x@gmail.com")]
    [InlineData("", "x@gmail.com")]
    [InlineData("not an email", "x@gmail.com")]
    [InlineData("jane@arbisoft.com", "")]
    [InlineData("jane@arbisoft.com", "nope")]
    [InlineData("jane@arbisoft.com", "JANE@arbisoft.com")]
    public async Task Create_InvalidOrPointlessInput_IsRefusedWithoutAWrite(string arbisoft, string mapped)
    {
        var result = await _service.CreateAsync(new EmailMappingInput(arbisoft, mapped), CancellationToken.None);

        Assert.False(result.Succeeded);
        await _mappings.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Create_AMappedEmailThatIsTooLong_IsRefused()
    {
        var result = await _service.CreateAsync(new EmailMappingInput("jane@arbisoft.com", new string('a', 300) + "@gmail.com"), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Create_ADuplicatePair_IsRefused_EvenWhenTypedInAnotherCase()
    {
        _mappings.ExistsAsync("jane@arbisoft.com", "x@gmail.com", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.CreateAsync(new EmailMappingInput("JANE@arbisoft.com", "X@Gmail.com"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Error, StringComparison.Ordinal);
        await _mappings.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Update_ChecksTheDuplicateIgnoringItself_AndKeepsTheCreatedTime()
    {
        var created = Now.AddDays(-9);
        _mappings.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new EmailMapping { Id = 3, CreatedOn = created });

        var result = await _service.UpdateAsync(3, new EmailMappingInput("jane@arbisoft.com", "New@Gmail.com"), CancellationToken.None);

        Assert.True(result.Succeeded);
        await _mappings.Received(1).ExistsAsync("jane@arbisoft.com", "new@gmail.com", 3, Arg.Any<CancellationToken>());
        await _mappings.Received(1).UpdateAsync(
            Arg.Is<EmailMapping>(m => m.Id == 3 && m.Email == "new@gmail.com" && m.EmailAsReceived == "New@Gmail.com" && m.CreatedOn == created),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AnUnknownMapping_Fails()
    {
        var result = await _service.UpdateAsync(9, new EmailMappingInput("jane@arbisoft.com", "x@gmail.com"), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Update_ToAPairThatExists_IsRefused()
    {
        _mappings.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new EmailMapping { Id = 3 });
        _mappings.ExistsAsync("jane@arbisoft.com", "x@gmail.com", 3, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.UpdateAsync(3, new EmailMappingInput("jane@arbisoft.com", "x@gmail.com"), CancellationToken.None);

        Assert.False(result.Succeeded);
        await _mappings.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task List_NormalizesTheSearchText_AndReturnsPagingInfo()
    {
        _mappings.ListAsync("jane", 2, EmailMappingService.PageSize, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<EmailMapping>([new EmailMapping { Id = 1, ArbisoftEmail = "jane@arbisoft.com", Email = "j@gmail.com", EmailAsReceived = "J@gmail.com" }], 120, 2, EmailMappingService.PageSize));

        var page = await _service.ListAsync("  JANE ", 2, CancellationToken.None);

        Assert.Equal(120, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasPrevious);
        Assert.True(page.HasNext);
        Assert.Equal("J@gmail.com", Assert.Single(page.Items).EmailAsReceived);
    }

    [Fact]
    public async Task List_WithBlankSearch_FiltersNothing()
    {
        _mappings.ListAsync(null, 1, EmailMappingService.PageSize, Arg.Any<CancellationToken>()).Returns(new PagedResult<EmailMapping>([], 0, 1, EmailMappingService.PageSize));

        var page = await _service.ListAsync("   ", 1, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalPages);
        Assert.False(page.HasNext);
    }
}
