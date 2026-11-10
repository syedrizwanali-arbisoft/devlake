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
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.AccessTokens;

public sealed class RevokeModel(IAccessTokenService accessTokens) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string Jti { get; set; } = string.Empty;

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    public AccessTokenDto? Token { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Token = await accessTokens.GetAsync(Jti, cancellationToken);
        return Token is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accessTokens.RevokeAsync(Jti, Reason, Admin, cancellationToken);
        if (!result.Succeeded)
        {
            Token = await accessTokens.GetAsync(Jti, cancellationToken);
            return Fail(result.Error);
        }

        Message = "Token revoked.";
        return RedirectToPage("Index");
    }
}
