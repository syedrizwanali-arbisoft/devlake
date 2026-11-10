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

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.ClientConfigs.OutboundUrls;

public sealed class IndexModel(IClientService clients, IClientConfigService configs) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public long ClientId { get; set; }

    public string ClientName { get; private set; } = string.Empty;

    public string BaseUrl { get; private set; } = string.Empty;

    public IReadOnlyList<ClientConfigOutboundUrlDto> Urls { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var client = await clients.GetAsync(ClientId, cancellationToken);
        var config = client is null ? null : await configs.GetAsync(ClientId, cancellationToken);
        if (client is null || config is null)
        {
            return NotFound();
        }

        (ClientName, BaseUrl) = (client.Client.Name, config.BaseUrl);
        Urls = await configs.ListOutboundUrlsAsync(ClientId, cancellationToken);
        return Page();
    }
}
