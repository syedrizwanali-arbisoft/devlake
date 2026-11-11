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
using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.BackgroundJobs;

public sealed class RequestProcessingJobTests : IDisposable
{
    private readonly IRequestProcessor _processor = Substitute.For<IRequestProcessor>();
    private readonly CapturedLogs _logs = new();
    private readonly ServiceProvider _services;

    public RequestProcessingJobTests()
    {
        _processor.ProcessPendingAsync(Arg.Any<CancellationToken>()).Returns(new ProcessingRunSummary(0, 0, 0, 0, 0, null));
        _services = new ServiceCollection().AddScoped(_ => _processor).AddLogging(builder => builder.AddProvider(_logs)).BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    private RequestProcessingJob Create(bool enabled = true, int intervalSeconds = 1)
    {
        return new RequestProcessingJob(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ProcessingOptions { Enabled = enabled, IntervalSeconds = intervalSeconds }),
            _services.GetRequiredService<ILogger<RequestProcessingJob>>());
    }

    [Fact]
    public async Task WhenTurnedOff_ItDoesNothing_AndSaysSo()
    {
        using var job = Create(enabled: false);

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;

        await _processor.DidNotReceiveWithAnyArgs().ProcessPendingAsync(default);
        Assert.Contains("turned off", _logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItRunsAtOnce_ThenOnEveryInterval_UntilItIsStopped()
    {
        using var job = Create();

        await job.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(2400));
        await job.StopAsync(CancellationToken.None);

        var runs = _processor.ReceivedCalls().Count();
        Assert.InRange(runs, 3, 4);
        Assert.Contains("every 1 seconds", _logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunThatFails_IsLogged_AndTheJobCarriesOnAtTheNextInterval()
    {
        _processor.ProcessPendingAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProcessingRunSummary>(new InvalidOperationException("the database is down")), Task.FromResult(new ProcessingRunSummary(0, 0, 0, 0, 0, null)));
        using var job = Create();

        await job.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        await job.StopAsync(CancellationToken.None);

        Assert.True(_processor.ReceivedCalls().Count() >= 2);
        Assert.Contains("A processing run failed", _logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunThatDidWork_IsLoggedWithItsCounts()
    {
        _processor.ProcessPendingAsync(Arg.Any<CancellationToken>()).Returns(new ProcessingRunSummary(5, 3, 1, 1, 2, "DevLake rejected our API token."));
        using var job = Create();

        await job.RunOnceAsync(CancellationToken.None);

        Assert.Contains("5 received, 3 completed, 1 requeued, 1 dead-lettered, 2 expired", _logs.All, StringComparison.Ordinal);
        Assert.Contains("stopped early: DevLake rejected our API token.", _logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIdleRun_IsNotLogged()
    {
        using var job = Create();

        await job.RunOnceAsync(CancellationToken.None);

        Assert.DoesNotContain("Processing run", _logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShutdownDuringARun_EndsTheJobCleanly()
    {
        using var stopping = new CancellationTokenSource();
        _processor.ProcessPendingAsync(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await stopping.CancelAsync();
            call.ArgAt<CancellationToken>(0).ThrowIfCancellationRequested();
            return new ProcessingRunSummary(0, 0, 0, 0, 0, null);
        });
        using var job = Create();

        await job.StartAsync(stopping.Token);
        await job.ExecuteTask!;

        Assert.Contains("stopped", _logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain("A processing run failed", _logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRun_UsesAScopeOfItsOwn()
    {
        var scopes = new List<IServiceProvider>();
        using var services = new ServiceCollection()
            .AddScoped(provider =>
            {
                scopes.Add(provider);
                return _processor;
            })
            .AddLogging()
            .BuildServiceProvider();
        using var job = new RequestProcessingJob(
            services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ProcessingOptions { IntervalSeconds = 1 }), services.GetRequiredService<ILogger<RequestProcessingJob>>());

        await job.RunOnceAsync(CancellationToken.None);
        await job.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, scopes.Distinct().Count());
    }
}
