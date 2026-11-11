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

using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class CallbackDelivery : ICallbackDelivery
{
    private readonly ISophiaClient _sophia;
    private readonly IClientConfigRepository _configs;
    private readonly IRequestLogRepository _requestLogs;
    private readonly IRequestAttemptLog _attempts;
    private readonly ProcessingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CallbackDelivery> _logger;

    public CallbackDelivery(
        ISophiaClient sophia,
        IClientConfigRepository configs,
        IRequestLogRepository requestLogs,
        IRequestAttemptLog attempts,
        IOptions<ProcessingOptions> options,
        TimeProvider timeProvider,
        ILogger<CallbackDelivery> logger)
    {
        _sophia = sophia;
        _configs = configs;
        _requestLogs = requestLogs;
        _attempts = attempts;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WorkerOutcome> DeliverAsync(RequestLog request, string payloadJson, CancellationToken stopping)
    {
        var config = await _configs.GetByClientRefIdAsync(request.ClientRefId, CancellationToken.None);
        if (config is not { IsActive: true })
        {
            return await EndAsync(request, RequestStatus.RevokedNoCallbackUrl);
        }

        var made = await _attempts.CountDeliveryAttemptsAsync(request.Id, CancellationToken.None);
        for (var attempt = made + 1; attempt <= config.MaxAttempts; attempt++)
        {
            var step = await TryOnceAsync(request, payloadJson);
            switch (step)
            {
                case Step.Delivered:
                    return await EndAsync(request, RequestStatus.Success);
                case Step.Revoked:
                    return await EndAsync(request, RequestStatus.RevokedNoCallbackUrl);
                case Step.Systemic:
                    await _requestLogs.UpdateStatusAsync(request.Id, RequestStatus.Queued, CancellationToken.None);
                    return WorkerOutcome.StopRun;
            }

            if (attempt < config.MaxAttempts && !await WaitAsync(attempt, stopping))
            {
                await _requestLogs.UpdateStatusAsync(request.Id, RequestStatus.Queued, CancellationToken.None);
                return WorkerOutcome.Requeue;
            }
        }

        _logger.LogWarning("Request {CorrelationId} failed: {Attempts} delivery attempts did not succeed", request.CorrelationId, config.MaxAttempts);
        return await EndAsync(request, RequestStatus.Failed);
    }

    private async Task<Step> TryOnceAsync(RequestLog request, string payloadJson)
    {
        try
        {
            var result = await _sophia.PostCallbackAsync(request.ClientRefId, request.EventType, payloadJson, request.CorrelationId, CancellationToken.None);
            await _attempts.RecordAsync(
                request.Id,
                result.Delivered ? DeliveryAttemptStatus.Delivered : DeliveryAttemptStatus.DeliveryFailed,
                result.ResponseBody,
                result.Delivered ? null : $"HTTP {result.StatusCode}",
                CancellationToken.None);
            return result.Delivered ? Step.Delivered : Step.Failed;
        }
        catch (CallbackNotConfiguredException)
        {
            _logger.LogWarning("Request {CorrelationId}: the callback was removed while it was being delivered", request.CorrelationId);
            return Step.Revoked;
        }
        catch (ExternalAuthenticationException exception)
        {
            _logger.LogError("Request {CorrelationId}: Sophia credentials problem: {Problem}", request.CorrelationId, exception.Message);
            return Step.Systemic;
        }
        catch (ExternalApiUnavailableException exception)
        {
            await _attempts.RecordAsync(request.Id, DeliveryAttemptStatus.DeliveryFailed, ProcessingConstants.EmptyJson, exception.Message, CancellationToken.None);
            return Step.Failed;
        }
    }

    /// <summary>Waits before the next try: the configured delay, doubled after each failure. Returns false when a shutdown cut the wait short.</summary>
    private async Task<bool> WaitAsync(int failedAttempts, CancellationToken stopping)
    {
        var delay = TimeSpan.FromSeconds(_options.DeliveryRetryDelaySeconds * Math.Pow(2, failedAttempts - 1));
        try
        {
            await Task.Delay(delay, _timeProvider, stopping);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<WorkerOutcome> EndAsync(RequestLog request, string status)
    {
        await _requestLogs.UpdateStatusAsync(request.Id, status, CancellationToken.None);
        return WorkerOutcome.Completed;
    }

    private enum Step
    {
        Delivered,
        Failed,
        Revoked,
        Systemic
    }
}
