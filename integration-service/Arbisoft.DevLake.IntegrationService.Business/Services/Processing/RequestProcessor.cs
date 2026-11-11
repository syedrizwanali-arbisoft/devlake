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

using System.Collections.Concurrent;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class RequestProcessor : IRequestProcessor
{
    private readonly IStaleRequestSweeper _sweeper;
    private readonly IRequestQueueConsumer _consumer;
    private readonly IProcessingPreflight _preflight;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProcessingOptions _processing;
    private readonly RabbitMqOptions _rabbitMq;
    private readonly ILogger<RequestProcessor> _logger;

    public RequestProcessor(
        IStaleRequestSweeper sweeper,
        IRequestQueueConsumer consumer,
        IProcessingPreflight preflight,
        IServiceScopeFactory scopeFactory,
        IOptions<ProcessingOptions> processing,
        IOptions<RabbitMqOptions> rabbitMq,
        ILogger<RequestProcessor> logger)
    {
        _sweeper = sweeper;
        _consumer = consumer;
        _preflight = preflight;
        _scopeFactory = scopeFactory;
        _processing = processing.Value;
        _rabbitMq = rabbitMq.Value;
        _logger = logger;
    }

    public async Task<ProcessingRunSummary> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var expired = await _sweeper.SweepAsync(cancellationToken);
        if (await _consumer.GetDepthAsync(cancellationToken) == 0)
        {
            return new ProcessingRunSummary(0, 0, 0, 0, expired, null);
        }

        var preflight = await _preflight.RunAsync(cancellationToken);
        if (!preflight.Ready)
        {
            _logger.LogError("Processing is paused and requests stay queued: {Problem}", preflight.Problem);
            return new ProcessingRunSummary(0, 0, 0, 0, expired, preflight.Problem);
        }

        await using var batch = await _consumer.ReceiveAsync(_rabbitMq.ReceiveBatchLimit, cancellationToken);
        var outcomes = await WorkAsync(batch.Deliveries, cancellationToken);
        return await SettleAsync(batch, outcomes, expired);
    }

    /// <summary>
    /// Processes the deliveries with at most <see cref="ProcessingOptions.MaxParallelRequests"/> at a time (ASY-5). Each request gets a scope of its own,
    /// so no database connection is shared between parallel requests. When one hits a systemic problem the rest are not started.
    /// </summary>
    private async Task<ConcurrentDictionary<ulong, WorkerOutcome>> WorkAsync(IReadOnlyList<QueueDelivery> deliveries, CancellationToken stopping)
    {
        var outcomes = new ConcurrentDictionary<ulong, WorkerOutcome>();
        var stopRun = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = _processing.MaxParallelRequests, CancellationToken = stopping };

        try
        {
            await Parallel.ForEachAsync(deliveries, options, async (delivery, token) =>
            {
                if (Volatile.Read(ref stopRun) == 1)
                {
                    return;
                }

                var outcome = await ProcessOneAsync(delivery, token);
                outcomes[delivery.DeliveryTag] = outcome;
                if (outcome == WorkerOutcome.StopRun)
                {
                    Interlocked.Exchange(ref stopRun, 1);
                }
            });
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            _logger.LogInformation("Processing was stopped; unfinished requests go back on the queue");
        }

        return outcomes;
    }

    private async Task<WorkerOutcome> ProcessOneAsync(QueueDelivery delivery, CancellationToken stopping)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IRequestWorker>().ProcessAsync(delivery, stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return WorkerOutcome.Requeue;
        }
        catch (Exception exception)
        {
            // A bug or a database problem in one request must not stop the others. The message goes back; the redelivery limit parks one that keeps failing.
            _logger.LogError(exception, "Request {CorrelationId} failed unexpectedly and will be tried again", delivery.Message.CorrelationId);
            return WorkerOutcome.Requeue;
        }
    }

    /// <summary>Settles every message after the work, one at a time because a channel is not safe for concurrent use. Work that finished is always acknowledged, even after a shutdown.</summary>
    private async Task<ProcessingRunSummary> SettleAsync(IQueueBatch batch, ConcurrentDictionary<ulong, WorkerOutcome> outcomes, int expired)
    {
        int completed = 0, requeued = 0, deadLettered = 0;
        string? halt = null;
        foreach (var delivery in batch.Deliveries)
        {
            var outcome = outcomes.TryGetValue(delivery.DeliveryTag, out var known) ? known : WorkerOutcome.Requeue;
            halt ??= outcome == WorkerOutcome.StopRun ? "A systemic problem stopped the run; the affected requests stay queued." : null;
            try
            {
                switch (outcome)
                {
                    case WorkerOutcome.Completed:
                        await batch.AckAsync(delivery, CancellationToken.None);
                        completed++;
                        break;
                    case WorkerOutcome.DeadLetter:
                        await batch.DeadLetterAsync(delivery, CancellationToken.None);
                        deadLettered++;
                        break;
                    default:
                        await batch.RequeueAsync(delivery, CancellationToken.None);
                        requeued++;
                        break;
                }
            }
            catch (QueueUnavailableException exception)
            {
                _logger.LogWarning(exception, "The broker was lost while settling messages; the rest return to the queue and repeat deliveries are skipped");
                break;
            }
        }

        return new ProcessingRunSummary(batch.Deliveries.Count, completed, requeued, deadLettered, expired, halt);
    }
}
