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

using System.Text;
using Arbisoft.DevLake.IntegrationService.API.Controllers;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Controllers;

public class AuthControllerTests
{
    private readonly IAuthService _authService = Substitute.For<IAuthService>();

    private AuthController CreateController(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return new AuthController(_authService) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static string Basic(string credentials) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

    [Fact]
    public async Task ReturnsTheTokenAndForbidsCaching()
    {
        var response = new TokenResponse("token", "Bearer", 3600, "stats:read");
        _authService.IssueTokenAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>()).Returns(TokenResult.Success(response));
        var controller = CreateController(Basic("sophia:secret"));

        var result = await controller.Token("client_credentials", CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task SplitsCredentialsOnTheFirstColonOnly()
    {
        _authService.IssueTokenAsync(default!).ReturnsForAnyArgs(TokenResult.Failure(TokenError.InvalidClient));

        await CreateController(Basic("sophia:se:cr:et")).Token("client_credentials", CancellationToken.None);

        await _authService.Received(1).IssueTokenAsync(
            Arg.Is<TokenRequest>(r => r.ClientId == "sophia" && r.ClientSecret == "se:cr:et"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic")]
    [InlineData("Basic !!!not-base64!!!")]
    [InlineData("Basic c29waGlh")] // "sophia", with no colon
    [InlineData("Basic OnNlY3JldA==")] // ":secret"
    [InlineData("Basic c29waGlhOg==")] // "sophia:"
    public async Task ReturnsInvalidClientForBadBasicCredentials(string? header)
    {
        var controller = CreateController(header);

        var result = await controller.Token("client_credentials", CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Basic", controller.Response.Headers.WWWAuthenticate);
        await _authService.DidNotReceiveWithAnyArgs().IssueTokenAsync(default!, default);
    }

    [Fact]
    public async Task ReturnsInvalidClientWhenTheServiceRejectsTheClient()
    {
        _authService.IssueTokenAsync(default!).ReturnsForAnyArgs(TokenResult.Failure(TokenError.InvalidClient));

        var result = await CreateController(Basic("sophia:wrong")).Token("client_credentials", CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ReturnsBadRequestForAnUnsupportedGrant()
    {
        _authService.IssueTokenAsync(default!).ReturnsForAnyArgs(TokenResult.Failure(TokenError.UnsupportedGrantType));

        var result = await CreateController(Basic("sophia:secret")).Token("password", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
