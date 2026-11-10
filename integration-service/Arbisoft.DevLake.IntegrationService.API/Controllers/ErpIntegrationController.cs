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

using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;
using Arbisoft.DevLake.IntegrationService.Business.Services.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>Gives DevLake the Workstream projects and their team leads.</summary>
[ApiController]
[Route("fetch")]
[Authorize(Policy = ApiScopes.ProjectsFetch)]
public sealed class ErpIntegrationController(IErpIntegrationService erpIntegration) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects(CancellationToken cancellationToken)
    {
        var result = await erpIntegration.GetProjectsAsync(cancellationToken);

        // Both failures are Workstream's side, so they are 502s; the error says which one it was.
        return result.Outcome == ProjectsOutcome.Success
            ? Ok(result.Response)
            : StatusCode(StatusCodes.Status502BadGateway, new { error = result.Error, workstream_token_rejected = result.Outcome == ProjectsOutcome.TokenRejected });
    }
}
