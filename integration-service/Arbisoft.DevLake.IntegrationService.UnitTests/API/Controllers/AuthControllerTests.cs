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

using System.Net;
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
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _controller = new AuthController(_authService) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        _controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
    }

    private void SetBasic(string credentials)
    {
        _controller.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    [Fact]
    public async Task Token_GoodRequest_Returns200WithTheTokenAndNoStoreHeaders()
    {
        SetBasic("sophia:s3cret");
        var token = new TokenResponse("jwt", "Bearer", 900, "stats:read");
        _authService.AuthenticateAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>()).Returns(AuthResult.Success(token));

        var result = await _controller.TokenAsync("client_credentials", CancellationToken.None);

        Assert.Same(token, Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl.ToString());
        await _authService.Received(1).AuthenticateAsync(new TokenRequest("sophia", "s3cret", "203.0.113.7"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_MissingBasicHeader_Returns401WithAChallenge_WithoutCallingTheService()
    {
        var result = await _controller.TokenAsync("client_credentials", CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(AuthConstants.InvalidClient, ((OAuthErrorResponse)unauthorized.Value!).Error);
        Assert.StartsWith("Basic", _controller.Response.Headers.WWWAuthenticate.ToString(), StringComparison.Ordinal);
        await _authService.DidNotReceiveWithAnyArgs().AuthenticateAsync(default!, default);
    }

    [Fact]
    public async Task Token_RefusedByTheService_Returns401WithTheSameBodyWhateverTheReason()
    {
        SetBasic("sophia:wrong");
        _authService.AuthenticateAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>()).Returns(AuthResult.Fail(AuthFailureReason.InvalidSecret));

        var result = await _controller.TokenAsync("client_credentials", CancellationToken.None);

        var body = (OAuthErrorResponse)Assert.IsType<UnauthorizedObjectResult>(result).Value!;
        Assert.Equal("invalid_client", body.Error);
        Assert.DoesNotContain("secret", body.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "invalid_request")]
    [InlineData("", "invalid_request")]
    [InlineData("password", "unsupported_grant_type")]
    [InlineData("Client_Credentials", "unsupported_grant_type")]
    public async Task Token_MissingOrUnsupportedGrantType_Returns400(string? grantType, string expectedError)
    {
        SetBasic("sophia:s3cret");

        var result = await _controller.TokenAsync(grantType, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(expectedError, ((OAuthErrorResponse)bad.Value!).Error);
        await _authService.DidNotReceiveWithAnyArgs().AuthenticateAsync(default!, default);
    }

    [Fact]
    public async Task Token_IPv6Caller_IsPassedAsItsOwnAddress()
    {
        SetBasic("sophia:s3cret");
        _controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");
        _authService.AuthenticateAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>()).Returns(AuthResult.Fail(AuthFailureReason.UnknownClient));

        await _controller.TokenAsync("client_credentials", CancellationToken.None);

        await _authService.Received(1).AuthenticateAsync(Arg.Is<TokenRequest>(r => r.CallerIp == "2001:db8::1"), Arg.Any<CancellationToken>());
    }
}
