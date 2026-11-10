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

using System.Net;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>
/// Turns queued stats requests into callbacks. Problems that affect every request (a rejected token, DevLake down)
/// halt the run and leave requests queued. Problems with one request mark only that request Failed.
/// </summary>
public sealed class RequestProcessor(
    IRequestQueueConsumer consumer,
    IRequestLogRepository requestLogs,
    IEmailMappingRepository emailMappings,
    ICallbackTargetResolver callbackTargets,
    IDevLakeClient devLake,
    ISophiaClient sophia,
    IOptions<ProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<RequestProcessor> logger) : IRequestProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DevLakeStatsCategory[] Categories =
        [DevLakeStatsCategory.Jira, DevLakeStatsCategory.Github, DevLakeStatsCategory.AiUsage, DevLakeStatsCategory.AiCost];

    private static readonly string AttemptBy = Environment.MachineName + "/RequestProcessingJob";

    public async Task<ProcessingRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await devLake.IsTokenValidAsync(cancellationToken: cancellationToken))
            {
                logger.LogError("DevLake rejected its API token. Requests stay queued until it is fixed.");
                return new ProcessingRunResult(true, null);
            }
        }
        catch (ExternalApiException ex)
        {
            logger.LogError(ex, "The DevLake token check failed. Requests stay queued.");
            return new ProcessingRunResult(true, null);
        }

        var result = await consumer.ConsumeAsync(ProcessAsync, Math.Max(options.Value.MaxMessagesPerRun, 1), cancellationToken);
        if (result.Processed + result.Requeued + result.DeadLettered > 0 || result.Halted)
        {
            logger.LogInformation("Processing run finished: {Result}.", result);
        }

        return new ProcessingRunResult(false, result);
    }

    public async Task ProcessAsync(QueuedRequest message, CancellationToken cancellationToken = default)
    {
        var request = Deserialize(message);
        var log = await requestLogs.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken)
            ?? throw new InvalidDataException($"Queued request {request.CorrelationId} has no request log.");

        // Delivery is at least once, so a request that already reached a final status is acknowledged and skipped.
        if (log.Status is RequestLogStatus.Success or RequestLogStatus.Failed or RequestLogStatus.RevokedNoCallbackUrl)
        {
            logger.LogInformation("Request {CorrelationId} is already {Status}; skipping it.", request.CorrelationId, log.Status);
            return;
        }

        CallbackTarget? target;
        try
        {
            // Checked again, in case the callback was removed while the request was queued.
            target = await callbackTargets.ResolveAsync(request.ClientRefId, request.EventType, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Request {CorrelationId} failed: the client config is incomplete.", request.CorrelationId);
            await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.Failed, cancellationToken);
            return;
        }

        if (target is null)
        {
            await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.RevokedNoCallbackUrl, cancellationToken);
            return;
        }

        await EnsureClientTokenAsync(target, request, cancellationToken);
        await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.Processing, cancellationToken);

        IReadOnlyList<EmployeeStatsDto> engineers;
        try
        {
            engineers = await GatherStatsAsync(request, cancellationToken);
        }
        catch (ExternalApiException ex) when (AffectsEveryRequest(ex))
        {
            await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.Queued, CancellationToken.None);
            throw new QueueProcessingHaltedException($"DevLake is unavailable: {ex.Message}", ex);
        }
        catch (ExternalApiException ex)
        {
            logger.LogError(ex, "Request {CorrelationId} failed while fetching stats from DevLake.", request.CorrelationId);
            await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.Failed, cancellationToken);
            return;
        }

        await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.Processed, cancellationToken);
        var payload = JsonSerializer.Serialize(new StatsCallbackPayload(request.CorrelationId, request.TimeFrame, engineers), JsonOptions);
        var delivered = await DeliverAsync(log, target, payload, request.CorrelationId, cancellationToken);
        await requestLogs.UpdateStatusAsync(log.Id, delivered ? RequestLogStatus.Success : RequestLogStatus.Failed, cancellationToken);
    }

    private static StatsRequest Deserialize(QueuedRequest message)
    {
        try
        {
            return JsonSerializer.Deserialize<StatsRequest>(message.Json, JsonOptions)
                ?? throw new InvalidDataException($"Queued message {message.CorrelationId} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Queued message {message.CorrelationId} is not a stats request.", ex);
        }
    }

    private async Task EnsureClientTokenAsync(CallbackTarget target, StatsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await sophia.GetAccessTokenAsync(target.Connection, cancellationToken);
        }
        catch (ExternalApiAuthenticationException ex)
        {
            throw new QueueProcessingHaltedException(
                $"No token for client config {target.Connection.ClientConfigId} (request {request.CorrelationId}): {ex.Message}", ex);
        }
    }

    private async Task<IReadOnlyList<EmployeeStatsDto>> GatherStatsAsync(StatsRequest request, CancellationToken cancellationToken)
    {
        using var throttle = new SemaphoreSlim(Math.Max(options.Value.MaxParallelDevLakeCalls, 1));
        var engineers = new List<EmployeeStatsDto>(request.Emails.Count);

        foreach (var email in request.Emails)
        {
            var mapped = await emailMappings.GetMappedEmailsAsync(email, cancellationToken);
            var emails = mapped.Prepend(email)
                .Select(e => e.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var calls = Categories.ToDictionary(
                category => category,
                category => emails.Select(e => FetchAsync(e, category, request, throttle, cancellationToken)).ToList());
            await Task.WhenAll(calls.Values.SelectMany(tasks => tasks));

            IReadOnlyList<EmailStats> Results(DevLakeStatsCategory category) => calls[category].Select(t => t.Result).ToList();
            engineers.Add(new EmployeeStatsDto(email, emails,
                Results(DevLakeStatsCategory.Jira), Results(DevLakeStatsCategory.Github),
                Results(DevLakeStatsCategory.AiUsage), Results(DevLakeStatsCategory.AiCost)));
        }

        return engineers;
    }

    private async Task<EmailStats> FetchAsync(
        string email, DevLakeStatsCategory category, StatsRequest request, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            return new EmailStats(email, await devLake.GetStatsAsync(email, category, request.TimeFrame, request.CorrelationId, cancellationToken));
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<bool> DeliverAsync(RequestLog log, CallbackTarget target, string payload, string correlationId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= target.MaxAttempts; attempt++)
        {
            CallbackResult result;
            try
            {
                result = await sophia.SendCallbackAsync(target.Connection, target.Method, target.Path, payload, correlationId, cancellationToken);
            }
            catch (ExternalApiAuthenticationException ex)
            {
                await requestLogs.UpdateStatusAsync(log.Id, RequestLogStatus.Queued, CancellationToken.None);
                throw new QueueProcessingHaltedException($"Lost the token for client config {target.Connection.ClientConfigId}: {ex.Message}", ex);
            }

            await requestLogs.AddAttemptAsync(new RequestResponseAttempt
            {
                RequestLogId = log.Id,
                ResponseJson = result.ResponseBody,
                AttemptStatus = result.Succeeded ? RequestLogStatus.Success : RequestLogStatus.Failed,
                FailureError = result.Error,
                AttemptBy = AttemptBy,
                AttemptOn = timeProvider.GetUtcNow().UtcDateTime,
            }, cancellationToken);

            if (result.Succeeded)
            {
                return true;
            }

            logger.LogWarning("Delivery attempt {Attempt} of {MaxAttempts} for request {CorrelationId} failed: {Error}",
                attempt, target.MaxAttempts, correlationId, result.Error);
            if (attempt < target.MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.DeliveryRetryDelaySeconds * attempt), timeProvider, cancellationToken);
            }
        }

        return false;
    }

    /// <summary>A rejected token, a server error or no response means DevLake itself is unwell, not this request.</summary>
    private static bool AffectsEveryRequest(ExternalApiException ex) =>
        ex is ExternalApiAuthenticationException || ex.StatusCode is null || (int)ex.StatusCode.Value >= 500
        || ex.StatusCode == HttpStatusCode.TooManyRequests || ex.StatusCode == HttpStatusCode.RequestTimeout;
}
