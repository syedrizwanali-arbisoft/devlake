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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class RequestService(
    IClientConfigRepository clientConfigs,
    IRequestLogRepository requestLogs,
    IRequestQueuePublisher publisher,
    TimeProvider timeProvider,
    ILogger<RequestService> logger) : IRequestService
{
    public const string NoCallbackUrlError = "Revoked - no callback URL";
    public const string QueueUnavailableError = "The request could not be queued. Try again later.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StatsSubmissionResult> QueueAsync(StatsSubmission submission, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var correlationId = Guid.NewGuid().ToString();
        var json = JsonSerializer.Serialize(new StatsRequest(
            correlationId, submission.ClientRefId, submission.ClientId, submission.EventType,
            submission.Emails, submission.TimeFrame.ToString(), now), JsonOptions);

        var callback = await clientConfigs.GetActiveOutboundUrlAsync(submission.ClientRefId, submission.EventType, cancellationToken);
        if (callback is null)
        {
            // Revoked requests keep their hash, but the idempotency check skips them, so the client can send again.
            await requestLogs.AddAsync(NewLog(submission, correlationId, RequestLogStatus.RevokedNoCallbackUrl, now), json, cancellationToken);
            return new StatsSubmissionResult(StatsSubmissionOutcome.NoCallbackUrl, correlationId, NoCallbackUrlError);
        }

        // Log first, so the processing job always finds the log of a message it takes from the queue.
        var logId = await requestLogs.AddAsync(NewLog(submission, correlationId, RequestLogStatus.Received, now), json, cancellationToken);
        try
        {
            await publisher.PublishAsync(correlationId, json, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Request {CorrelationId} could not be queued.", correlationId);
            await requestLogs.UpdateStatusAsync(logId, RequestLogStatus.Failed, CancellationToken.None);
            return new StatsSubmissionResult(StatsSubmissionOutcome.QueueUnavailable, correlationId, QueueUnavailableError);
        }

        await requestLogs.UpdateStatusAsync(logId, RequestLogStatus.Queued, cancellationToken);
        return new StatsSubmissionResult(StatsSubmissionOutcome.Accepted, correlationId, string.Empty);
    }

    private static RequestLog NewLog(StatsSubmission submission, string correlationId, string status, DateTime now) => new()
    {
        ClientRefId = submission.ClientRefId,
        CorrelationId = correlationId,
        Url = submission.Url,
        Status = status,
        EventType = submission.EventType,
        RequestHash = submission.RequestHash,
        CreatedOn = now,
    };
}
