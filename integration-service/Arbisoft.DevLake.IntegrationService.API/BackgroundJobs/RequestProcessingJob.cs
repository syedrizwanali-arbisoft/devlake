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

using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.API.BackgroundJobs;

/// <summary>
/// Runs the request processor on a schedule. A run that fails is logged and the job carries on at the next tick, so one bad run never ends the job.
/// On shutdown it stops taking new work; the processor puts unfinished requests back on the queue (INT-10).
/// </summary>
public sealed class RequestProcessingJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProcessingOptions _options;
    private readonly ILogger<RequestProcessingJob> _logger;

    public RequestProcessingJob(IServiceScopeFactory scopeFactory, IOptions<ProcessingOptions> options, ILogger<RequestProcessingJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("The request processing job is turned off; requests stay queued");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));
        _logger.LogInformation("The request processing job runs every {Seconds} seconds", _options.IntervalSeconds);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("The request processing job stopped");
        }
    }

    /// <summary>One run in a scope of its own. Public so a test can drive a single run without waiting for the timer.</summary>
    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var summary = await scope.ServiceProvider.GetRequiredService<IRequestProcessor>().ProcessPendingAsync(stoppingToken);
            if (summary.DidWork)
            {
                _logger.LogInformation(
                    "Processing run: {Received} received, {Completed} completed, {Requeued} requeued, {DeadLettered} dead-lettered, {Expired} expired{Halt}",
                    summary.Received,
                    summary.Completed,
                    summary.Requeued,
                    summary.DeadLettered,
                    summary.Expired,
                    summary.HaltReason is null ? string.Empty : $", stopped early: {summary.HaltReason}");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A processing run failed; the job will try again at the next interval");
        }
    }
}
