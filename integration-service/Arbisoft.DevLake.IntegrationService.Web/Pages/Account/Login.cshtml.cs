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

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class LoginModel(IOptions<AdminOptions> options, IClientSecretHasher hasher, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool IsConfigured => options.Value.IsConfigured;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var admin = options.Value;
        var usernameMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Username ?? string.Empty), Encoding.UTF8.GetBytes(admin.Username));

        // The hash is checked even for a wrong username, so the response time doesn't reveal which part was wrong.
        var passwordMatches = admin.IsConfigured && hasher.Verify(Password ?? string.Empty, admin.PasswordHash);
        if (!admin.IsConfigured || !usernameMatches || !passwordMatches)
        {
            logger.LogWarning("Failed admin sign-in from {RemoteIp}.", HttpContext.Connection.RemoteIpAddress);
            ModelState.AddModelError(string.Empty, "The username or password is wrong.");
            Password = string.Empty;
            return Page();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, admin.Username)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        logger.LogInformation("Admin {Username} signed in.", admin.Username);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
