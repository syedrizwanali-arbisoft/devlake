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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class StaleRequestSweeper : IStaleRequestSweeper
{
    private const int BatchSize = 100;
    private const string ExpiredReason = "The request was not processed before it expired from the queue.";

    private static readonly string[] UnfinishedStatuses = [RequestStatus.Received, RequestStatus.Queued, RequestStatus.Processing];

    private readonly IRequestLogRepository _requestLogs;
    private readonly IRequestAttemptLog _attempts;
    private readonly RabbitMqOptions _rabbitMq;
    private readonly ProcessingOptions _processing;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StaleRequestSweeper> _logger;

    public StaleRequestSweeper(
        IRequestLogRepository requestLogs,
        IRequestAttemptLog attempts,
        IOptions<RabbitMqOptions> rabbitMq,
        IOptions<ProcessingOptions> processing,
        TimeProvider timeProvider,
        ILogger<StaleRequestSweeper> logger)
    {
        _requestLogs = requestLogs;
        _attempts = attempts;
        _rabbitMq = rabbitMq.Value;
        _processing = processing.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var lifetime = TimeSpan.FromSeconds(_rabbitMq.MessageTtlSeconds + _processing.StaleGraceSeconds);
        var stale = await _requestLogs.ListStaleAsync(UnfinishedStatuses, _timeProvider.GetUtcNow() - lifetime, BatchSize, cancellationToken);

        foreach (var request in stale)
        {
            await _attempts.RecordAsync(request.Id, DeliveryAttemptStatus.Expired, ProcessingConstants.EmptyJson, ExpiredReason, cancellationToken);
            await _requestLogs.UpdateStatusAsync(request.Id, RequestStatus.Failed, cancellationToken);
            _logger.LogWarning("Request {CorrelationId} expired before it was processed and was marked failed", request.CorrelationId);
        }

        return stale.Count;
    }
}
