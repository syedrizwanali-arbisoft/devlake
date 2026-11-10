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

public sealed class DetailsModel(IClientService clients, IClientConfigService configs) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public ClientDetailsDto? Details { get; private set; }

    public ClientConfigDto? Config { get; private set; }

    public int OutboundUrlCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Details = await clients.GetAsync(Id, cancellationToken);
        if (Details is null)
        {
            return NotFound();
        }

        Config = await configs.GetAsync(Id, cancellationToken);
        OutboundUrlCount = Config is null ? 0 : (await configs.ListOutboundUrlsAsync(Id, cancellationToken)).Count;
        return Page();
    }

    public async Task<IActionResult> OnPostScopesAsync(long[] scopeIds, CancellationToken cancellationToken)
    {
        var result = await clients.SetScopesAsync(Id, scopeIds, Admin, cancellationToken);
        if (!result.Succeeded)
        {
            Details = await clients.GetAsync(Id, cancellationToken);
            return Details is null ? NotFound() : Fail(result.Error);
        }

        Message = "Scopes saved.";
        return RedirectToPage(new { id = Id });
    }
}
