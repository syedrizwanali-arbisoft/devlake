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

public class AccessTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IAccessTokenRepository _tokens = Substitute.For<IAccessTokenRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly AccessTokenService _service;

    public AccessTokenServiceTests()
    {
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Client { Id = 1, ClientId = "sophia", Name = "Sophia" });
        _service = new AccessTokenService(_tokens, _clients, new FakeTimeProvider(Now));
    }

    private static AccessToken Token(long id, DateTimeOffset expiresOn, DateTimeOffset? revokedOn = null) =>
        new() { Id = id, Jti = "jti-" + id, ClientRefId = 1, GrantedScopes = "stats:read", IssuedOn = Now.AddHours(-1), ExpiresOn = expiresOn, RevokedOn = revokedOn, IssuedToIp = "203.0.113.7" };

    [Fact]
    public async Task ListRecent_LabelsEachTokenAndLooksUpEachClientOnce()
    {
        _tokens.ListAsync(null, 1, 50, Arg.Any<CancellationToken>()).Returns(new PagedResult<AccessToken>(
            [Token(1, Now.AddHours(1)), Token(2, Now.AddHours(-1)), Token(3, Now.AddHours(1), Now.AddMinutes(-5))], 3, 1, 50));

        var list = await _service.ListRecentAsync(null, 50, CancellationToken.None);

        Assert.Equal(["Active", "Expired", "Revoked"], list.Select(t => t.Status));
        Assert.All(list, t => Assert.Equal("sophia", t.ClientId));
        await _clients.Received(1).GetByIdAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListRecent_LimitsTheRowsAskedFor()
    {
        _tokens.ListAsync(Arg.Any<long?>(), 1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<AccessToken>([], 0, 1, 200));

        await _service.ListRecentAsync(4, 100_000, CancellationToken.None);

        await _tokens.Received(1).ListAsync(4, 1, 200, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_AnUnknownToken_IsNull()
    {
        Assert.Null(await _service.GetAsync("nope", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revoke_WithoutAReason_IsRefused(string reason)
    {
        var result = await _service.RevokeAsync("jti-1", reason, "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _tokens.DidNotReceiveWithAnyArgs().RevokeAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Revoke_AReasonThatIsTooLong_IsRefused()
    {
        var result = await _service.RevokeAsync("jti-1", new string('x', AccessTokenService.MaxReasonLength + 1), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Revoke_AnUnknownToken_Fails()
    {
        var result = await _service.RevokeAsync("nope", "leaked", "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Revoke_AnAlreadyRevokedToken_IsRefused()
    {
        _tokens.GetByJtiAsync("jti-3", Arg.Any<CancellationToken>()).Returns(Token(3, Now.AddHours(1), Now.AddMinutes(-5)));

        var result = await _service.RevokeAsync("jti-3", "leaked", "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _tokens.DidNotReceiveWithAnyArgs().RevokeAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Revoke_RecordsTheReasonAndWhoDidIt()
    {
        _tokens.GetByJtiAsync("jti-1", Arg.Any<CancellationToken>()).Returns(Token(1, Now.AddHours(1)));
        _tokens.RevokeAsync(1, Arg.Any<string>(), Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.RevokeAsync("jti-1", " leaked in chat ", "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        await _tokens.Received(1).RevokeAsync(1, "leaked in chat (revoked by admin)", Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoke_WhenSomeoneElseRevokedItFirst_ReportsIt()
    {
        _tokens.GetByJtiAsync("jti-1", Arg.Any<CancellationToken>()).Returns(Token(1, Now.AddHours(1)));
        _tokens.RevokeAsync(1, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.RevokeAsync("jti-1", "leaked", "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
    }
}
