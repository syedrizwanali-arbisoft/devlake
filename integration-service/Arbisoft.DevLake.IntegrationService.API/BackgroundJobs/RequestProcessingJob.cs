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

using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.API.BackgroundJobs;

/// <summary>
/// Runs the request processor once at startup and then every Processing:IntervalSeconds. The processor is resolved only
/// when the job runs, so a missing RabbitMQ or DevLake setting is logged on each run instead of stopping the API.
/// </summary>
public sealed class RequestProcessingJob(
    IServiceProvider services,
    IOptions<ProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<RequestProcessingJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The request processing job is turned off (Processing:Enabled).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(options.Value.IntervalSeconds, 1));
        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    /// <summary>A failed run is logged and the next tick tries again, so one bad run never stops the job.</summary>
    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await services.GetRequiredService<IRequestProcessor>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A request processing run failed. The next run will try again.");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
