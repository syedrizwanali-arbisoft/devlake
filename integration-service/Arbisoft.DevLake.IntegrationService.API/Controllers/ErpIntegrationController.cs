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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Projects;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>Serves Workstream's project and team lead data to DevLake, so DevLake never calls Workstream itself.</summary>
[ApiController]
[ApiVersionNeutral]
[Route("fetch")]
[Authorize(Policy = ScopeNames.ProjectsFetch)]
public sealed class ErpIntegrationController : ControllerBase
{
    public const string TokenInvalidCode = "workstream_token_invalid";

    private readonly IErpIntegrationService _service;

    public ErpIntegrationController(IErpIntegrationService service)
    {
        _service = service;
    }

    /// <summary>
    /// Returns every Workstream project with its team lead. The whole list comes back in one answer, because the caller needs all of it to tell what
    /// exists; if Workstream fails at any point nothing is returned, so a partial list is never mistaken for the full one.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("projects")]
    [ProducesResponseType<IReadOnlyList<ProjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var result = await _service.FetchProjectsAsync(HttpContext.TraceIdentifier, cancellationToken);
        if (result.Outcome == ProjectsFetchOutcome.TokenInvalid)
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status502BadGateway, "Workstream rejected this service's API token.");
            problem.Extensions["code"] = TokenInvalidCode;
            return StatusCode(StatusCodes.Status502BadGateway, problem);
        }

        return Ok(result.Projects);
    }
}
