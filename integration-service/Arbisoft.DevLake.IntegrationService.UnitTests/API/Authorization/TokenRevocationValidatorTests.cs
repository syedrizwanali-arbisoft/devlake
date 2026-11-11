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

using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Authorization;

public class TokenRevocationValidatorTests
{
    private readonly IAccessTokenRepository _accessTokens = Substitute.For<IAccessTokenRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly TokenRevocationValidator _validator;

    public TokenRevocationValidatorTests()
    {
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Client { Id = 1, IsActive = true });
        _validator = new TokenRevocationValidator(_accessTokens, _clients, NullLogger<TokenRevocationValidator>.Instance);
    }

    private static TokenValidatedContext ContextFor(string? jti)
    {
        var handler = new JsonWebTokenHandler();
        var claims = new Dictionary<string, object>();
        if (jti is not null)
        {
            claims[JwtRegisteredClaimNames.Jti] = jti;
        }

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256)
        });
        var scheme = new AuthenticationScheme("Bearer", displayName: null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(new DefaultHttpContext(), scheme, new JwtBearerOptions()) { SecurityToken = new JsonWebToken(token) };
    }

    [Fact]
    public async Task IssuedAndNotRevoked_Passes()
    {
        _accessTokens.GetByJtiAsync("jti-1", Arg.Any<CancellationToken>()).Returns(new AccessToken { Jti = "jti-1", ClientRefId = 1 });
        var context = ContextFor("jti-1");

        await _validator.ValidateAsync(context);

        Assert.False(context.Result?.Failure is not null);
    }

    [Fact]
    public async Task OfAClientThatIsNoLongerActive_IsRejected()
    {
        _clients.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new Client { Id = 2, IsActive = false });
        _accessTokens.GetByJtiAsync("jti-2", Arg.Any<CancellationToken>()).Returns(new AccessToken { Jti = "jti-2", ClientRefId = 2 });
        var context = ContextFor("jti-2");

        await _validator.ValidateAsync(context);

        Assert.Contains("not active", context.Result!.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfAClientThatNoLongerExists_IsRejected()
    {
        _accessTokens.GetByJtiAsync("jti-3", Arg.Any<CancellationToken>()).Returns(new AccessToken { Jti = "jti-3", ClientRefId = 99 });
        var context = ContextFor("jti-3");

        await _validator.ValidateAsync(context);

        Assert.NotNull(context.Result!.Failure);
    }

    [Fact]
    public async Task Revoked_IsRejected()
    {
        _accessTokens.GetByJtiAsync("jti-1", Arg.Any<CancellationToken>()).Returns(new AccessToken { Jti = "jti-1", RevokedOn = DateTimeOffset.UtcNow });
        var context = ContextFor("jti-1");

        await _validator.ValidateAsync(context);

        Assert.Contains("revoked", context.Result!.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NeverIssuedByThisService_IsRejected()
    {
        _accessTokens.GetByJtiAsync("ghost", Arg.Any<CancellationToken>()).Returns((AccessToken?)null);
        var context = ContextFor("ghost");

        await _validator.ValidateAsync(context);

        Assert.Contains("not issued", context.Result!.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAJti_IsRejectedWithoutQueryingTheDatabase()
    {
        var context = ContextFor(jti: null);

        await _validator.ValidateAsync(context);

        Assert.NotNull(context.Result!.Failure);
        await _accessTokens.DidNotReceiveWithAnyArgs().GetByJtiAsync(default!, default);
    }
}
