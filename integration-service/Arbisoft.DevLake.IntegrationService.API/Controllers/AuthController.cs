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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>The OAuth2 client credentials endpoint. Errors use the RFC 6749 body, as OAuth client libraries expect.</summary>
[ApiController]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private const string UnknownIp = "unknown";
    private const string ChallengeValue = "Basic realm=\"integration-service\", charset=\"UTF-8\"";

    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Exchanges a Client ID and secret (HTTP Basic) for a bearer token.</summary>
    [HttpPost("/auth/token/")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<OAuthErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<OAuthErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<OAuthErrorResponse>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> TokenAsync([FromForm(Name = "grant_type")] string? grantType, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        if (!BasicAuthorizationHeader.TryParse(Request.Headers.Authorization, out var clientId, out var clientSecret))
        {
            return InvalidClient();
        }

        if (!Request.HasFormContentType)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new OAuthErrorResponse(AuthConstants.InvalidRequest, "The Content-Type must be application/x-www-form-urlencoded."));
        }

        if (string.IsNullOrEmpty(grantType))
        {
            return BadRequest(new OAuthErrorResponse(AuthConstants.InvalidRequest, "The grant_type parameter is required."));
        }

        if (grantType != AuthConstants.ClientCredentialsGrant)
        {
            return BadRequest(new OAuthErrorResponse(AuthConstants.UnsupportedGrantType, "Only the client_credentials grant is supported."));
        }

        var callerIp = CallerIp();
        var result = await _authService.AuthenticateAsync(new TokenRequest(clientId, clientSecret, callerIp), cancellationToken);
        return result.Succeeded ? Ok(result.Token) : InvalidClient();
    }

    private string CallerIp()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        return address is null ? UnknownIp : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private UnauthorizedObjectResult InvalidClient()
    {
        Response.Headers[HeaderNames.WWWAuthenticate] = ChallengeValue;
        return Unauthorized(new OAuthErrorResponse(AuthConstants.InvalidClient, "Client authentication failed."));
    }
}
