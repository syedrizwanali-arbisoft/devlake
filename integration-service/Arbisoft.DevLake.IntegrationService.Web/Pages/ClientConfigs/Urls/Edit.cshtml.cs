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

using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.ClientConfigs.Urls;

public sealed class EditModel(IClientConfigService configs) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public long ClientRefId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long UrlId { get; set; }

    [BindProperty]
    public UrlForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var url = await configs.GetUrlAsync(UrlId, cancellationToken);
        if (url is null)
        {
            return NotFound();
        }

        Form = new UrlForm { EventType = url.EventType, Method = url.Method, Path = url.Path, IsActive = url.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await configs.UpdateUrlAsync(UrlId, Form.ToInput(), Admin, cancellationToken);
        if (!result.Succeeded)
        {
            return Fail(result.Error);
        }

        Message = "URL saved.";
        return RedirectToPage("/ClientConfigs/Details", new { clientRefId = ClientRefId });
    }
}
