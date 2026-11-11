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

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Arbisoft.DevLake.IntegrationService.API.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.Business.Validation;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>
/// Accepts requests for an engineer's stats. The work is asynchronous: each call returns a correlation ID at once, and the result is delivered
/// to the client's callback API later. Poll <c>GET /api/v1/requests/poll/{uuid}</c> for the status.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/engineers")]
[Authorize(Policy = ScopeNames.StatsRead)]
public sealed class StatsController : ControllerBase
{
    private readonly IRequestHandler _handler;

    public StatsController(IRequestHandler handler)
    {
        _handler = handler;
    }

    /// <summary>Requests the stats of one engineer for a time frame such as 24h, 1w, 30d, 11M or 1y.</summary>
    /// <param name="email">The engineer's Arbisoft email.</param>
    /// <param name="timeFrame">The time frame, with a case-sensitive unit: h, d, w, M or y.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("{email}/stats")]
    [EnableRateLimiting(RateLimitPolicies.SingleStats)]
    [ProducesResponseType<StatsAcknowledgement>(StatusCodes.Status200OK)]
    [ProducesResponseType<StatsAcknowledgement>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetStatsAsync(
        [FromRoute, ArbisoftEmail] string email,
        [FromQuery(Name = "tf"), Required, TimeFrameValue] string? timeFrame,
        CancellationToken cancellationToken)
    {
        var result = await _handler.HandleSingleAsync(ClientId(), new StatsRequest(email, timeFrame!), cancellationToken);
        return result.Outcome switch
        {
            IntakeOutcome.Accepted => Ok(result.Acknowledgement),
            IntakeOutcome.NoCallbackUrl => UnprocessableEntity(result.Acknowledgement),
            _ => Unauthorized()
        };
    }

    /// <summary>Requests the stats of up to 20 engineers under one correlation ID.</summary>
    /// <param name="request">The emails and the time frame.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpPost("stats/batch")]
    [EnableRateLimiting(RateLimitPolicies.BatchStats)]
    [ProducesResponseType<BatchStatsAcknowledgement>(StatusCodes.Status200OK)]
    [ProducesResponseType<BatchStatsAcknowledgement>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PostBatchAsync([FromBody] BatchStatsRequest request, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleBatchAsync(ClientId(), request, cancellationToken);
        return result.Outcome switch
        {
            IntakeOutcome.Accepted => Ok(result.Acknowledgement),
            IntakeOutcome.NoCallbackUrl => UnprocessableEntity(result.Acknowledgement),
            _ => Unauthorized()
        };
    }

    private string ClientId() => User.FindFirstValue(TokenClaims.Client) ?? string.Empty;
}
