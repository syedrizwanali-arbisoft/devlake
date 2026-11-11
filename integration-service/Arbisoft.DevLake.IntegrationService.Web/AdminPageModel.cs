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

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arbisoft.DevLake.IntegrationService.Web;

public abstract class AdminPageModel : PageModel
{
    /// <summary>The signed-in admin, recorded as createdBy or modifiedBy.</summary>
    protected string Admin => User.Identity?.Name ?? "unknown";

    [TempData]
    public string? Message { get; set; }

    public static string Utc(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    protected IActionResult Fail(string? error)
    {
        ModelState.AddModelError(string.Empty, error ?? "The change failed.");
        return Page();
    }
}
