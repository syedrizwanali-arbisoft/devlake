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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class RequestHandler : IRequestHandler
{
    private const string OkStatus = "200";
    private const string UnprocessableStatus = "422";
    private const string SingleRequestUrl = "/api/v1/engineers/{email}/stats?tf={timeFrame}";
    private const string BatchRequestUrl = "/api/v1/engineers/stats/batch";

    private readonly IClientRepository _clients;
    private readonly IIdempotencyCheck _idempotency;
    private readonly IRequestService _requestService;
    private readonly IOptionsSnapshot<RequestsOptions> _options;

    public RequestHandler(IClientRepository clients, IIdempotencyCheck idempotency, IRequestService requestService, IOptionsSnapshot<RequestsOptions> options)
    {
        _clients = clients;
        _idempotency = idempotency;
        _requestService = requestService;
        _options = options;
    }

    public async Task<IntakeResult<StatsAcknowledgement>> HandleSingleAsync(string clientId, StatsRequest request, CancellationToken cancellationToken)
    {
        var intake = await HandleAsync(clientId, RequestEventTypes.EngineerStats, [request.Email], request.TimeFrame, SingleRequestUrl, cancellationToken);
        if (intake.Outcome == IntakeOutcome.UnknownClient)
        {
            return new IntakeResult<StatsAcknowledgement>(IntakeOutcome.UnknownClient, null);
        }

        var acknowledgement = new StatsAcknowledgement(StatusOf(intake.Outcome), intake.Emails[0], intake.CorrelationId, ErrorOf(intake.Outcome));
        return new IntakeResult<StatsAcknowledgement>(intake.Outcome, acknowledgement);
    }

    public async Task<IntakeResult<BatchStatsAcknowledgement>> HandleBatchAsync(string clientId, BatchStatsRequest request, CancellationToken cancellationToken)
    {
        var intake = await HandleAsync(clientId, RequestEventTypes.EngineerStatsBatch, request.Emails!, request.TimeFrame!, BatchRequestUrl, cancellationToken);
        if (intake.Outcome == IntakeOutcome.UnknownClient)
        {
            return new IntakeResult<BatchStatsAcknowledgement>(IntakeOutcome.UnknownClient, null);
        }

        var acknowledgement = new BatchStatsAcknowledgement(StatusOf(intake.Outcome), intake.Emails, intake.CorrelationId, ErrorOf(intake.Outcome));
        return new IntakeResult<BatchStatsAcknowledgement>(intake.Outcome, acknowledgement);
    }

    private async Task<Intake> HandleAsync(
        string clientId, string eventType, IEnumerable<string> rawEmails, string rawTimeFrame, string requestUrl, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByClientIdAsync(clientId, cancellationToken);
        if (client is not { IsActive: true })
        {
            return new Intake(IntakeOutcome.UnknownClient, string.Empty, []);
        }

        var domains = _options.Value.AllowedEmailDomains;
        var emails = rawEmails.Select(email => EmailNormalizer.Normalize(email, domains)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var timeFrame = TimeFrame.TryParse(rawTimeFrame, out var parsed, out var error) ? parsed : throw new ArgumentException(error, nameof(rawTimeFrame));

        var key = _idempotency.ComputeKey(client.Id, eventType, timeFrame, emails);
        var duplicate = await _idempotency.FindDuplicateAsync(key, cancellationToken);
        if (duplicate is not null)
        {
            return new Intake(IntakeOutcome.Accepted, duplicate.CorrelationId, emails);
        }

        var result = await _requestService.SubmitAsync(new RequestSubmission(client.Id, eventType, requestUrl, emails, timeFrame, key), cancellationToken);
        var outcome = result.Outcome == SubmissionOutcome.NoCallbackUrl ? IntakeOutcome.NoCallbackUrl : IntakeOutcome.Accepted;
        return new Intake(outcome, result.CorrelationId, emails);
    }

    private static string StatusOf(IntakeOutcome outcome) => outcome == IntakeOutcome.NoCallbackUrl ? UnprocessableStatus : OkStatus;

    private static string ErrorOf(IntakeOutcome outcome) => outcome == IntakeOutcome.NoCallbackUrl ? RequestStatus.RevokedNoCallbackUrl : string.Empty;

    private sealed record Intake(IntakeOutcome Outcome, string CorrelationId, IReadOnlyList<string> Emails);
}
