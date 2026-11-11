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

using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class RequestService : IRequestService
{
    private const int MaxConflictRetries = 3;

    private readonly IRequestLogRepository _requestLogs;
    private readonly IClientConfigRepository _clientConfigs;
    private readonly IRequestQueuePublisher _publisher;
    private readonly IDbSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestService> _logger;

    public RequestService(
        IRequestLogRepository requestLogs,
        IClientConfigRepository clientConfigs,
        IRequestQueuePublisher publisher,
        IDbSession session,
        TimeProvider timeProvider,
        ILogger<RequestService> logger)
    {
        _requestLogs = requestLogs;
        _clientConfigs = clientConfigs;
        _publisher = publisher;
        _session = session;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SubmissionResult> SubmitAsync(RequestSubmission submission, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var body = JsonSerializer.Serialize(new QueuedStatsRequest(correlationId, submission.EventType, submission.Emails, submission.TimeFrame.Text, _timeProvider.GetUtcNow()));

        if (!await HasActiveCallbackAsync(submission, cancellationToken))
        {
            await RecordRevokedAsync(submission, correlationId, body, cancellationToken);
            return new SubmissionResult(SubmissionOutcome.NoCallbackUrl, correlationId);
        }

        for (var attempt = 1; attempt <= MaxConflictRetries; attempt++)
        {
            if (await TryQueueAsync(submission, correlationId, body, cancellationToken))
            {
                _logger.LogInformation("Queued request {CorrelationId} for {Count} email(s)", correlationId, submission.Emails.Count);
                return new SubmissionResult(SubmissionOutcome.Queued, correlationId);
            }

            var winner = await _requestLogs.GetByIdempotencyKeyAsync(submission.IdempotencyKey, cancellationToken);
            if (winner is not null)
            {
                _logger.LogInformation("Request matched {CorrelationId} while it was being queued", winner.CorrelationId);
                return new SubmissionResult(SubmissionOutcome.Duplicate, winner.CorrelationId);
            }
        }

        throw new InvalidOperationException("The request could not be queued because its idempotency key kept changing hands.");
    }

    /// <summary>
    /// Writes the log and the body first, then publishes, then marks the request Queued, all in one transaction. A request that loses a race on the
    /// unique idempotency key stops before it publishes, and a rejected publish rolls the rows back, so no request is left half-recorded.
    /// </summary>
    private async Task<bool> TryQueueAsync(RequestSubmission submission, string correlationId, string body, CancellationToken cancellationToken)
    {
        await _session.BeginTransactionAsync(cancellationToken);
        var id = await _requestLogs.TryInsertAsync(NewLog(submission, correlationId, RequestStatus.Received, submission.IdempotencyKey), cancellationToken);
        if (id is null)
        {
            await _session.RollbackAsync(cancellationToken);
            return false;
        }

        await _requestLogs.InsertJsonAsync(new RequestLogJson { RequestLogId = id.Value, Json = body }, cancellationToken);
        await _publisher.PublishAsync(new QueueMessage(correlationId, submission.EventType, body), cancellationToken);
        await _requestLogs.UpdateStatusAsync(id.Value, RequestStatus.Queued, cancellationToken);
        await _session.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>A revoked request is final and holds no idempotency key, so the client can send it again once a callback URL exists.</summary>
    private async Task RecordRevokedAsync(RequestSubmission submission, string correlationId, string body, CancellationToken cancellationToken)
    {
        await _session.BeginTransactionAsync(cancellationToken);
        var id = await _requestLogs.InsertAsync(NewLog(submission, correlationId, RequestStatus.RevokedNoCallbackUrl, idempotencyKey: null), cancellationToken);
        await _requestLogs.InsertJsonAsync(new RequestLogJson { RequestLogId = id, Json = body }, cancellationToken);
        await _session.CommitAsync(cancellationToken);
        _logger.LogWarning("Request {CorrelationId} revoked: client {ClientRefId} has no active callback URL for {EventType}", correlationId, submission.ClientRefId, submission.EventType);
    }

    private async Task<bool> HasActiveCallbackAsync(RequestSubmission submission, CancellationToken cancellationToken)
    {
        var config = await _clientConfigs.GetByClientRefIdAsync(submission.ClientRefId, cancellationToken);
        if (config is not { IsActive: true })
        {
            return false;
        }

        return await _clientConfigs.GetActiveOutboundUrlAsync(config.Id, submission.EventType, cancellationToken) is not null;
    }

    private RequestLog NewLog(RequestSubmission submission, string correlationId, string status, string? idempotencyKey) => new()
    {
        ClientRefId = submission.ClientRefId,
        CorrelationId = correlationId,
        Url = submission.RequestUrl,
        Status = status,
        EventType = submission.EventType,
        CreatedOn = _timeProvider.GetUtcNow(),
        IdempotencyKey = idempotencyKey
    };
}
