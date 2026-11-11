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

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.ClientConfigs;

public sealed class EditModel(IClientConfigService configs) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public long ClientRefId { get; set; }

    [BindProperty]
    public ConfigForm Form { get; set; } = new();

    public string ClientName { get; private set; } = string.Empty;

    public bool HasClientSecret { get; private set; }

    public bool HasApiToken { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var details = await LoadAsync(cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        if (details.Config is { } c)
        {
            Form = new ConfigForm
            {
                AuthType = c.AuthType,
                BaseUrl = c.BaseUrl,
                TokenUrl = c.TokenUrl,
                OutboundClientId = c.OutboundClientId,
                OutboundScope = c.OutboundScope,
                TokenExpiresOn = c.TokenExpiresOn is { } expires ? DateOnly.FromDateTime(expires.UtcDateTime) : null,
                MaxAttempts = c.MaxAttempts,
                IsActive = c.IsActive
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await configs.SaveAsync(ClientRefId, Form.ToInput(), Admin, cancellationToken);
        if (!result.Succeeded)
        {
            return await FailWithDetailsAsync(result.Error, cancellationToken);
        }

        Message = "Outbound config saved.";
        return RedirectToPage("Details", new { clientRefId = ClientRefId });
    }

    private async Task<IActionResult> FailWithDetailsAsync(string? error, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Fail(error);
    }

    private async Task<ClientConfigDetailsDto?> LoadAsync(CancellationToken cancellationToken)
    {
        var details = await configs.GetAsync(ClientRefId, cancellationToken);
        if (details is not null)
        {
            ClientName = details.ClientName;
            HasClientSecret = details.Config?.HasClientSecret ?? false;
            HasApiToken = details.Config?.HasApiToken ?? false;
        }

        return details;
    }
}
