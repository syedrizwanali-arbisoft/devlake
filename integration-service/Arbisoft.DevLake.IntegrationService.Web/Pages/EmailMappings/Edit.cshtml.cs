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

namespace Arbisoft.DevLake.IntegrationService.Web.Pages.EmailMappings;

public sealed class EditModel(IEmailMappingService mappings) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public MappingForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var mapping = await mappings.GetAsync(Id, cancellationToken);
        if (mapping is null)
        {
            return NotFound();
        }

        Form = new MappingForm { ArbisoftEmail = mapping.ArbisoftEmail, Email = mapping.EmailAsReceived };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await mappings.UpdateAsync(Id, new EmailMappingInput(Form.ArbisoftEmail, Form.Email), cancellationToken);
        if (!result.Succeeded)
        {
            return Fail(result.Error);
        }

        Message = "Mapping saved.";
        return RedirectToPage("Index");
    }
}
