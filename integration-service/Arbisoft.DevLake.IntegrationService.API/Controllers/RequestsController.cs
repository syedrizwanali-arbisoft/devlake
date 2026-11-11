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

using System.Security.Claims;
using Arbisoft.DevLake.IntegrationService.API.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>Lets a client check on a stats request it made. The answer is synchronous: it reports where the request is now.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/requests")]
[Authorize(Policy = ScopeNames.StatsRead)]
public sealed class RequestsController : ControllerBase
{
    private readonly IRequestStatusService _statusService;

    public RequestsController(IRequestStatusService statusService)
    {
        _statusService = statusService;
    }

    /// <summary>Returns the current status of a request: Queued, Processing, Success, Failure or Revoked.</summary>
    /// <param name="uuid">The <c>correlation_id</c> from the response to the original request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("poll/{uuid}")]
    [EnableRateLimiting(RateLimitPolicies.RequestPoll)]
    [ProducesResponseType<RequestStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PollAsync([FromRoute] Guid uuid, CancellationToken cancellationToken)
    {
        var clientId = User.FindFirstValue(TokenClaims.Client) ?? string.Empty;
        var status = await _statusService.GetStatusAsync(clientId, uuid, cancellationToken);
        return status is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "No request with that ID was found.")
            : Ok(status);
    }
}
