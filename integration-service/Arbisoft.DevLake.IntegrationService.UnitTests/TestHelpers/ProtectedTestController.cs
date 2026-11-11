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

using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Endpoints that exist only in tests, to exercise authentication and scope checks before real protected endpoints exist.</summary>
[ApiController]
public sealed class ProtectedTestController : ControllerBase
{
    [HttpGet("/test/stats")]
    [Authorize(Policy = ScopeNames.StatsRead)]
    public IActionResult Stats() => Ok();

    [HttpGet("/test/projects")]
    [Authorize(Policy = ScopeNames.ProjectsFetch)]
    public IActionResult Projects() => Ok();

    /// <summary>Has no attribute at all, so the fallback policy decides.</summary>
    [HttpGet("/test/unmarked")]
    public IActionResult Unmarked() => Ok();
}
