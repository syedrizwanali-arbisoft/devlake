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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class AccessTokenServiceTests
{
    private static readonly DateTime Now = AccessTokenBuilder.Now;

    private readonly IAccessTokenRepository _tokens = Substitute.For<IAccessTokenRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly AccessTokenService _service;

    public AccessTokenServiceTests()
    {
        _clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ClientBuilder().Build()]);
        _tokens.GetByJtiAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AccessToken?)null);
        _tokens.GetByJtiAsync("live", Arg.Any<CancellationToken>()).Returns(new AccessTokenBuilder().WithJti("live").Build());
        _tokens.GetByJtiAsync("gone", Arg.Any<CancellationToken>()).Returns(new AccessTokenBuilder().WithJti("gone").Revoked().Build());
        _service = new AccessTokenService(_tokens, _clients, new FakeTimeProvider(new DateTimeOffset(Now)));
    }

    [Fact]
    public async Task ListRecent_ShowsTheClientAndStatus()
    {
        _tokens.GetRecentAsync(200, Arg.Any<CancellationToken>()).Returns([
            new AccessTokenBuilder().WithJti("a").Build(),
            new AccessTokenBuilder().WithJti("b").Expired().Build(),
            new AccessTokenBuilder().WithJti("c").ForClient(99).Revoked().Build()]);

        var tokens = await _service.ListRecentAsync(200);

        Assert.Equal(["Active", "Expired", "Revoked"], tokens.Select(t => t.Status));
        Assert.Equal(["sophia", "sophia", "(unknown)"], tokens.Select(t => t.ClientId));
    }

    [Fact]
    public async Task Revoke_RecordsTheReasonAndTheAdmin()
    {
        var result = await _service.RevokeAsync("live", " leaked in a log ", "admin");

        Assert.True(result.Succeeded);
        await _tokens.Received(1).RevokeAsync("live", "leaked in a log (revoked by admin)", Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("live", "", "reason")]
    [InlineData("gone", "why", "already revoked")]
    [InlineData("missing", "why", "not found")]
    public async Task Revoke_RejectsBadRequests(string jti, string reason, string mentions)
    {
        var result = await _service.RevokeAsync(jti, reason, "admin");

        Assert.Contains(mentions, result.Error);
        await _tokens.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Get_ReturnsTheTokenWithItsClientOrNull()
    {
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());

        var token = await _service.GetAsync("live");

        Assert.Equal(("live", "sophia", "Active"), (token!.Jti, token.ClientId, token.Status));
        Assert.Null(await _service.GetAsync("missing"));
    }
}
