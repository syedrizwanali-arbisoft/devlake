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

using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.RequestLogs;

public sealed class DetailsModel(IRequestLogService requestLogs) : AdminPageModel
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public RequestLogDetailsDto? Details { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Details = await requestLogs.GetAsync(Id, cancellationToken);
        return Details is null ? NotFound() : Page();
    }

    /// <summary>Indents JSON for reading. Anything that isn't JSON, such as an HTML error page, is shown as is.</summary>
    public static string Pretty(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
