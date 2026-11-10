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
using Arbisoft.DevLake.IntegrationService.API.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

[ApiController]
[Route("api/v1/requests")]
[Authorize(Policy = ApiScopes.StatsRead)]
public sealed class RequestsController(IRequestStatusService requestStatusService) : ControllerBase
{
    [HttpGet("poll/{uuid}")]
    [EnableRateLimiting(RateLimitPolicies.Poll)]
    public async Task<IActionResult> Poll(string uuid, CancellationToken cancellationToken)
    {
        var clientId = User.FindFirst(TokenClaimNames.ClientId)?.Value ?? string.Empty;
        var result = await requestStatusService.GetStatusAsync(clientId, uuid, cancellationToken);

        return result.Outcome switch
        {
            RequestStatusOutcome.Found => Ok(result.Response),
            RequestStatusOutcome.InvalidId => BadRequest(new { correlation_id = uuid, error = "The correlation ID is not a valid UUID." }),
            _ => NotFound(new { correlation_id = uuid, error = "No request was found with this correlation ID." }),
        };
    }
}
