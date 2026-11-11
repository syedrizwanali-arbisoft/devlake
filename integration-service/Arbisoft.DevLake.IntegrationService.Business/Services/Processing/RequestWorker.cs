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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class RequestWorker : IRequestWorker
{
    private readonly IRequestLogRepository _requestLogs;
    private readonly IEmailMappingRepository _mappings;
    private readonly ICallbackAvailability _callbacks;
    private readonly IEngineerStatsCollector _collector;
    private readonly ICallbackDelivery _delivery;
    private readonly IRequestAttemptLog _attempts;
    private readonly ILogger<RequestWorker> _logger;

    public RequestWorker(
        IRequestLogRepository requestLogs,
        IEmailMappingRepository mappings,
        ICallbackAvailability callbacks,
        IEngineerStatsCollector collector,
        ICallbackDelivery delivery,
        IRequestAttemptLog attempts,
        ILogger<RequestWorker> logger)
    {
        _requestLogs = requestLogs;
        _mappings = mappings;
        _callbacks = callbacks;
        _collector = collector;
        _delivery = delivery;
        _attempts = attempts;
        _logger = logger;
    }

    public async Task<WorkerOutcome> ProcessAsync(QueueDelivery delivery, CancellationToken stopping)
    {
        var correlationId = delivery.Message.CorrelationId;
        var request = await _requestLogs.GetByCorrelationIdAsync(correlationId, stopping);
        if (request is null)
        {
            // The API publishes before it commits, so the log can briefly be missing. Put the message back; if it never appears the redelivery limit parks it.
            _logger.LogWarning("Request {CorrelationId} has no log row yet; leaving it queued", correlationId);
            return WorkerOutcome.Requeue;
        }

        if (IsFinished(request.Status))
        {
            _logger.LogInformation("Request {CorrelationId} was already finished; acknowledging the repeat delivery", correlationId);
            return WorkerOutcome.Completed;
        }

        if (!QueuedRequestReader.TryRead(delivery.Message.Json, out var readable, out var problem))
        {
            return await RejectUnreadableAsync(request, problem!);
        }

        if (!await _callbacks.IsAvailableAsync(request.ClientRefId, request.EventType, stopping))
        {
            _logger.LogWarning("Request {CorrelationId} revoked: its callback URL was removed while it was queued", correlationId);
            return await EndAsync(request, RequestStatus.RevokedNoCallbackUrl);
        }

        await _requestLogs.UpdateStatusAsync(request.Id, RequestStatus.Processing, stopping);
        return await CollectAndDeliverAsync(request, readable!, stopping);
    }

    private async Task<WorkerOutcome> CollectAndDeliverAsync(RequestLog request, ReadableRequest readable, CancellationToken stopping)
    {
        string payload;
        try
        {
            payload = await BuildPayloadAsync(request.CorrelationId, readable, stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            await RestoreQueuedAsync(request);
            return WorkerOutcome.Requeue;
        }
        catch (Exception exception) when (exception is ExternalAuthenticationException or ExternalApiUnavailableException)
        {
            _logger.LogWarning("Request {CorrelationId} could not be collected now: {Problem}", request.CorrelationId, exception.Message);
            await RestoreQueuedAsync(request);
            return WorkerOutcome.StopRun;
        }
        catch (ExternalApiException exception)
        {
            return await FailAsync(request, exception.Message);
        }

        return await _delivery.DeliverAsync(request, payload, stopping);
    }

    private async Task<string> BuildPayloadAsync(string correlationId, ReadableRequest readable, CancellationToken stopping)
    {
        // The database is used one call at a time, here, before anything runs in parallel: a scope's connection must never be shared between tasks (DAT-16).
        var engineers = new List<(string Email, IReadOnlyList<string> Mapped)>();
        foreach (var email in readable.Request.Emails)
        {
            engineers.Add((email, await _mappings.GetMappedEmailsAsync(email, stopping)));
        }

        var collections = engineers.Select(engineer => _collector.CollectAsync(engineer.Email, engineer.Mapped, readable.Query, correlationId, stopping)).ToList();
        await UpstreamFailures.AwaitAllAsync(collections);

        var request = readable.Request;
        var payload = new CallbackPayload(correlationId, collections.Select(task => task.Result).ToList(), request.TimeFrame, request.From, request.To);
        return JsonSerializer.Serialize(payload);
    }

    private async Task<WorkerOutcome> RejectUnreadableAsync(RequestLog request, string problem)
    {
        _logger.LogError("Request {CorrelationId} cannot be read and is moved to the dead-letter queue: {Problem}", request.CorrelationId, problem);
        await _attempts.RecordAsync(request.Id, DeliveryAttemptStatus.FetchFailed, ProcessingConstants.EmptyJson, problem, CancellationToken.None);
        await _requestLogs.UpdateStatusAsync(request.Id, RequestStatus.Failed, CancellationToken.None);
        return WorkerOutcome.DeadLetter;
    }

    private async Task<WorkerOutcome> FailAsync(RequestLog request, string reason)
    {
        _logger.LogWarning("Request {CorrelationId} failed: {Reason}", request.CorrelationId, reason);
        await _attempts.RecordAsync(request.Id, DeliveryAttemptStatus.FetchFailed, ProcessingConstants.EmptyJson, reason, CancellationToken.None);
        return await EndAsync(request, RequestStatus.Failed);
    }

    private async Task<WorkerOutcome> EndAsync(RequestLog request, string status)
    {
        await _requestLogs.UpdateStatusAsync(request.Id, status, CancellationToken.None);
        return WorkerOutcome.Completed;
    }

    private async Task RestoreQueuedAsync(RequestLog request)
    {
        await _requestLogs.UpdateStatusAsync(request.Id, RequestStatus.Queued, CancellationToken.None);
    }

    private static bool IsFinished(string status) => status is RequestStatus.Success or RequestStatus.Failed or RequestStatus.RevokedNoCallbackUrl;
}
