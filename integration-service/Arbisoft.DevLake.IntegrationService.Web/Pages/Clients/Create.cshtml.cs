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

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.Clients;

public sealed class CreateModel(IClientService clients) : AdminPageModel
{
    [BindProperty]
    public ClientForm Form { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await clients.CreateAsync(
            new ClientInput(Form.ClientId, Form.Name, Form.IsActive, Form.AccessTokenLifetimeSeconds), Admin, cancellationToken);
        if (!result.Succeeded)
        {
            return Fail(result.Error);
        }

        Message = $"Client '{Form.ClientId}' created. Create a secret and assign scopes next.";
        return RedirectToPage("Details", new { id = result.Value });
    }
}
