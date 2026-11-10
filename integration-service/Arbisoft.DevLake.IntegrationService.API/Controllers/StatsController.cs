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
using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.API.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>Accepts stats requests and queues them. Results go to the client's callback later.</summary>
[ApiController]
[Route("api/v1/engineers")]
[Authorize(Policy = ApiScopes.StatsRead)]
public sealed class StatsController(IRequestHandler requestHandler) : ControllerBase
{
    [HttpGet("{email}/stats")]
    [EnableRateLimiting(RateLimitPolicies.Stats)]
    public async Task<IActionResult> GetStats(string email, [FromQuery(Name = "tf")] string? timeFrame, CancellationToken cancellationToken)
    {
        var result = await requestHandler.SubmitSingleAsync(ClientId, email, timeFrame, RequestUrl, cancellationToken);
        var status = StatusFor(result.Outcome);
        return StatusCode(status, new StatsAcknowledgement(Code(status), email, result.CorrelationId, result.Error));
    }

    [HttpPost("stats/batch")]
    [EnableRateLimiting(RateLimitPolicies.BatchStats)]
    public async Task<IActionResult> GetBatchStats([FromBody] BatchStatsRequest? request, CancellationToken cancellationToken)
    {
        var emails = request?.Emails ?? [];
        var result = await requestHandler.SubmitBatchAsync(ClientId, request?.Emails, request?.TimeFrame, RequestUrl, cancellationToken);
        var status = StatusFor(result.Outcome);
        return StatusCode(status, new BatchStatsAcknowledgement(Code(status), emails, result.CorrelationId, result.Error));
    }

    private string ClientId => User.FindFirst(TokenClaimNames.ClientId)?.Value ?? string.Empty;

    private string RequestUrl => Request.Path + Request.QueryString;

    private static int StatusFor(StatsSubmissionOutcome outcome) => outcome switch
    {
        StatsSubmissionOutcome.Accepted or StatsSubmissionOutcome.Duplicate => StatusCodes.Status200OK,
        StatsSubmissionOutcome.Invalid => StatusCodes.Status400BadRequest,
        StatsSubmissionOutcome.UnknownClient => StatusCodes.Status403Forbidden,
        StatsSubmissionOutcome.NoCallbackUrl => StatusCodes.Status422UnprocessableEntity,
        StatsSubmissionOutcome.QueueUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string Code(int status) => status.ToString(CultureInfo.InvariantCulture);
}
