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

public sealed class ConfigForm
{
    public string AuthType { get; set; } = "oauth2";
    public string BaseUrl { get; set; } = string.Empty;
    public string? TokenUrl { get; set; }
    public string? OutboundClientId { get; set; }
    public string? OutboundClientSecret { get; set; }
    public string? OutboundScope { get; set; }
    public string? ApiToken { get; set; }
    public DateTime? TokenExpiresOn { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public bool IsActive { get; set; } = true;
}

public sealed class EditModel(IClientService clients, IClientConfigService configs) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public long ClientId { get; set; }

    [BindProperty]
    public ConfigForm Form { get; set; } = new();

    public string ClientName { get; private set; } = string.Empty;

    public ClientConfigDto? Existing { get; private set; }

    public IReadOnlyList<string> AuthTypes => ClientConfigService.AuthTypes;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Existing is { } c)
        {
            Form = new ConfigForm
            {
                AuthType = c.AuthType, BaseUrl = c.BaseUrl, TokenUrl = c.TokenUrl, OutboundClientId = c.OutboundClientId,
                OutboundScope = c.OutboundScope, TokenExpiresOn = c.TokenExpiresOn, MaxAttempts = c.MaxAttempts, IsActive = c.IsActive,
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        var result = await configs.SaveAsync(ClientId, new ClientConfigInput(
            Form.AuthType, Form.BaseUrl, Form.TokenUrl, Form.OutboundClientId, Form.OutboundClientSecret, Form.OutboundScope,
            Form.ApiToken, Form.TokenExpiresOn, Form.MaxAttempts, Form.IsActive), Admin, cancellationToken);
        if (!result.Succeeded)
        {
            return Fail(result.Error);
        }

        Message = "Outbound config saved.";
        return RedirectToPage("/Clients/Details", new { id = ClientId });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var client = await clients.GetAsync(ClientId, cancellationToken);
        if (client is null)
        {
            return false;
        }

        ClientName = client.Client.Name;
        Existing = await configs.GetAsync(ClientId, cancellationToken);
        return true;
    }
}
