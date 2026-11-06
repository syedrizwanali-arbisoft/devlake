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

using System.Net.Http.Headers;
using System.Text;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

[ApiController]
[Route("auth/token")]
[AllowAnonymous]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>OAuth2 client credentials token endpoint (RFC 6749, section 4.4).</summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm(Name = "grant_type")] string? grantType, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!TryReadBasicCredentials(out var clientId, out var clientSecret))
        {
            return InvalidClient();
        }

        var callerIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var result = await authService.IssueTokenAsync(
            new TokenRequest(clientId, clientSecret, grantType, callerIp), cancellationToken);

        return result.Error switch
        {
            TokenError.None => Ok(result.Response),
            TokenError.UnsupportedGrantType => BadRequest(new { error = "unsupported_grant_type" }),
            _ => InvalidClient(),
        };
    }

    private UnauthorizedObjectResult InvalidClient()
    {
        Response.Headers.WWWAuthenticate = "Basic";
        return Unauthorized(new { error = "invalid_client" });
    }

    private bool TryReadBasicCredentials(out string clientId, out string clientSecret)
    {
        clientId = clientSecret = string.Empty;

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        // Only the first colon separates the two values, because a secret may contain one.
        var separator = decoded.IndexOf(':');
        if (separator <= 0 || separator == decoded.Length - 1)
        {
            return false;
        }

        clientId = decoded[..separator];
        clientSecret = decoded[(separator + 1)..];
        return true;
    }
}
