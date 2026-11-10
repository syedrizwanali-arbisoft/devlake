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

using Arbisoft.DevLake.IntegrationService.API.BackgroundJobs;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.BackgroundJobs;

public class RequestProcessingJobTests
{
    private readonly IRequestProcessor _processor = Substitute.For<IRequestProcessor>();
    private readonly FakeTimeProvider _time = new();
    private readonly SemaphoreSlim _runs = new(0);

    public RequestProcessingJobTests()
    {
        _processor.RunAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _runs.Release();
            return Task.FromResult(new ProcessingRunResult(false, null));
        });
    }

    private RequestProcessingJob Create(bool enabled = true) => new(
        new ServiceCollection().AddSingleton(_processor).BuildServiceProvider(),
        Options.Create(new ProcessingOptions { Enabled = enabled, IntervalSeconds = 60 }), _time, NullLogger<RequestProcessingJob>.Instance);

    private async Task WaitForRunAsync() => Assert.True(await _runs.WaitAsync(TimeSpan.FromSeconds(5)), "The job didn't run.");

    [Fact]
    public async Task RunsAtStartAndThenOncePerInterval()
    {
        using var job = Create();
        await job.StartAsync(CancellationToken.None);

        await WaitForRunAsync();
        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(await _runs.WaitAsync(TimeSpan.FromMilliseconds(100)));
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForRunAsync();
        _time.Advance(TimeSpan.FromSeconds(60));
        await WaitForRunAsync();

        await job.StopAsync(CancellationToken.None);
        await _processor.Received(3).RunAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsRunningAfterAFailedRun()
    {
        _processor.RunAsync(Arg.Any<CancellationToken>()).Returns(
            _ => { _runs.Release(); throw new InvalidOperationException("broker down"); },
            _ => { _runs.Release(); return Task.FromResult(new ProcessingRunResult(false, null)); });
        using var job = Create();
        await job.StartAsync(CancellationToken.None);

        await WaitForRunAsync();
        _time.Advance(TimeSpan.FromSeconds(60));
        await WaitForRunAsync();

        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DoesNothingWhenTurnedOff()
    {
        using var job = Create(enabled: false);
        await job.StartAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(await _runs.WaitAsync(TimeSpan.FromMilliseconds(200)));
        await job.StopAsync(CancellationToken.None);
        await _processor.DidNotReceiveWithAnyArgs().RunAsync(default);
    }

    [Fact]
    public async Task StopsCleanly()
    {
        using var job = Create();
        await job.StartAsync(CancellationToken.None);
        await WaitForRunAsync();

        var stop = job.StopAsync(CancellationToken.None);

        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task RunOnce_LogsAProcessorThatCannotBeBuilt()
    {
        var services = new ServiceCollection()
            .AddSingleton<IRequestProcessor>(_ => throw new InvalidOperationException("RabbitMq:Username is not set."))
            .BuildServiceProvider();
        var job = new RequestProcessingJob(services, Options.Create(new ProcessingOptions { Enabled = true }), _time,
            NullLogger<RequestProcessingJob>.Instance);

        await job.RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunOnce_SwallowsErrorsSoTheLoopSurvives()
    {
        _processor.RunAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());

        await Create().RunOnceAsync(CancellationToken.None);
    }
}
