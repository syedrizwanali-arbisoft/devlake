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
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public sealed class RequestProcessorTests : IDisposable
{
    private readonly IStaleRequestSweeper _sweeper = Substitute.For<IStaleRequestSweeper>();
    private readonly IRequestQueueConsumer _consumer = Substitute.For<IRequestQueueConsumer>();
    private readonly IProcessingPreflight _preflight = Substitute.For<IProcessingPreflight>();
    private readonly IQueueBatch _batch = Substitute.For<IQueueBatch>();
    private readonly Dictionary<ulong, Func<CancellationToken, Task<WorkerOutcome>>> _outcomes = [];
    private ServiceProvider _services = null!;
    private int _scopesCreated;

    public RequestProcessorTests()
    {
        _sweeper.SweepAsync(Arg.Any<CancellationToken>()).Returns(0);
        _consumer.GetDepthAsync(Arg.Any<CancellationToken>()).Returns(3);
        _consumer.ReceiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_batch);
        _preflight.RunAsync(Arg.Any<CancellationToken>()).Returns(PreflightResult.Go);
    }

    public void Dispose()
    {
        _services?.Dispose();
    }

    private RequestProcessor Create(int maxParallel = 4, int receiveLimit = 10)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestWorker>(_ =>
        {
            Interlocked.Increment(ref _scopesCreated);
            return new ScriptedWorker(_outcomes);
        });
        _services = services.BuildServiceProvider();
        return new RequestProcessor(
            _sweeper,
            _consumer,
            _preflight,
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ProcessingOptions { MaxParallelRequests = maxParallel }),
            Options.Create(new RabbitMqOptions { ReceiveBatchLimit = receiveLimit }),
            NullLogger<RequestProcessor>.Instance);
    }

    private List<QueueDelivery> Deliveries(params WorkerOutcome[] outcomes)
    {
        var list = new List<QueueDelivery>();
        for (var i = 0; i < outcomes.Length; i++)
        {
            var tag = (ulong)(i + 1);
            var outcome = outcomes[i];
            _outcomes[tag] = _ => Task.FromResult(outcome);
            list.Add(new QueueDelivery(new QueueMessage($"c{tag}", "engineer-stats", "{}"), tag, false, 0));
        }

        _batch.Deliveries.Returns(list);
        return list;
    }

    private sealed class ScriptedWorker : IRequestWorker
    {
        private readonly Dictionary<ulong, Func<CancellationToken, Task<WorkerOutcome>>> _outcomes;

        public ScriptedWorker(Dictionary<ulong, Func<CancellationToken, Task<WorkerOutcome>>> outcomes)
        {
            _outcomes = outcomes;
        }

        public Task<WorkerOutcome> ProcessAsync(QueueDelivery delivery, CancellationToken stopping) => _outcomes[delivery.DeliveryTag](stopping);
    }

    [Fact]
    public async Task AnEmptyQueue_DoesNothingBeyondTheSweep_NotEvenTheChecksAgainstDevLakeAndSophia()
    {
        _consumer.GetDepthAsync(Arg.Any<CancellationToken>()).Returns(0);
        _sweeper.SweepAsync(Arg.Any<CancellationToken>()).Returns(2);

        var summary = await Create().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(new ProcessingRunSummary(0, 0, 0, 0, 2, null), summary);
        Assert.True(summary.DidWork);
        await _preflight.DidNotReceiveWithAnyArgs().RunAsync(default);
        await _consumer.DidNotReceiveWithAnyArgs().ReceiveAsync(default, default);
    }

    [Fact]
    public async Task AnIdleRun_DidNoWork()
    {
        _consumer.GetDepthAsync(Arg.Any<CancellationToken>()).Returns(0);

        Assert.False((await Create().ProcessPendingAsync(CancellationToken.None)).DidWork);
    }

    [Fact]
    public async Task TheStaleSweepComesFirst_ThenTheDepthCheck_ThenThePreflight_ThenTheReceive()
    {
        Deliveries(WorkerOutcome.Completed);

        await Create().ProcessPendingAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            _sweeper.SweepAsync(Arg.Any<CancellationToken>());
            _consumer.GetDepthAsync(Arg.Any<CancellationToken>());
            _preflight.RunAsync(Arg.Any<CancellationToken>());
            _consumer.ReceiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task WhenThePreflightFails_NothingIsTakenOffTheQueue_SoNoMessageIsDisturbed()
    {
        _preflight.RunAsync(Arg.Any<CancellationToken>()).Returns(PreflightResult.Stop("DevLake rejected our API token."));

        var summary = await Create().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal("DevLake rejected our API token.", summary.HaltReason);
        Assert.Equal(0, summary.Received);
        await _consumer.DidNotReceiveWithAnyArgs().ReceiveAsync(default, default);
    }

    [Fact]
    public async Task TheReceiveLimitFromTheBrokerSettingsIsUsed()
    {
        Deliveries(WorkerOutcome.Completed);

        await Create(receiveLimit: 7).ProcessPendingAsync(CancellationToken.None);

        await _consumer.Received(1).ReceiveAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachOutcome_IsSettledTheRightWay()
    {
        var deliveries = Deliveries(WorkerOutcome.Completed, WorkerOutcome.DeadLetter, WorkerOutcome.Requeue);

        var summary = await Create().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(new ProcessingRunSummary(3, 1, 1, 1, 0, null), summary);
        await _batch.Received(1).AckAsync(deliveries[0], Arg.Any<CancellationToken>());
        await _batch.Received(1).DeadLetterAsync(deliveries[1], Arg.Any<CancellationToken>());
        await _batch.Received(1).RequeueAsync(deliveries[2], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMessageIsAcknowledgedOnlyAfterItsWorkIsDone()
    {
        var release = new TaskCompletionSource<WorkerOutcome>();
        var deliveries = Deliveries(WorkerOutcome.Completed);
        _outcomes[1] = _ => release.Task;

        var run = Create().ProcessPendingAsync(CancellationToken.None);
        await Task.Delay(100);
        await _batch.DidNotReceiveWithAnyArgs().AckAsync(default!, default);

        release.SetResult(WorkerOutcome.Completed);
        await run;

        await _batch.Received(1).AckAsync(deliveries[0], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoMoreRequestsThanTheConfiguredNumber_AreProcessedAtOnce()
    {
        Deliveries(WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed);
        var running = 0;
        var peak = 0;
        foreach (var tag in _outcomes.Keys.ToList())
        {
            _outcomes[tag] = async token =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                await Task.Delay(80, token);
                Interlocked.Decrement(ref running);
                return WorkerOutcome.Completed;
            };
        }

        var summary = await Create(maxParallel: 2).ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(6, summary.Completed);
        Assert.Equal(2, peak);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
        }
        while (value > seen && Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    [Fact]
    public async Task EveryRequest_GetsAScopeOfItsOwn_SoNoDatabaseConnectionIsSharedBetweenParallelRequests()
    {
        Deliveries(WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed);

        await Create().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(3, _scopesCreated);
    }

    [Fact]
    public async Task ASystemicProblem_StopsTheRestFromStarting_AndPutsThemBack()
    {
        var deliveries = Deliveries(WorkerOutcome.StopRun, WorkerOutcome.Completed, WorkerOutcome.Completed);

        var summary = await Create(maxParallel: 1).ProcessPendingAsync(CancellationToken.None);

        Assert.NotNull(summary.HaltReason);
        Assert.Equal(0, summary.Completed);
        Assert.Equal(3, summary.Requeued);
        foreach (var delivery in deliveries)
        {
            await _batch.Received(1).RequeueAsync(delivery, Arg.Any<CancellationToken>());
        }

        await _batch.DidNotReceiveWithAnyArgs().AckAsync(default!, default);
    }

    [Fact]
    public async Task OneRequestThatThrows_IsPutBack_AndTheOthersAreStillProcessed()
    {
        var deliveries = Deliveries(WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed);
        _outcomes[2] = _ => Task.FromException<WorkerOutcome>(new InvalidOperationException("database exploded"));

        var summary = await Create().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(2, summary.Completed);
        Assert.Equal(1, summary.Requeued);
        await _batch.Received(1).RequeueAsync(deliveries[1], Arg.Any<CancellationToken>());
        await _batch.Received(1).AckAsync(deliveries[0], Arg.Any<CancellationToken>());
        await _batch.Received(1).AckAsync(deliveries[2], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShutdown_StillAcknowledgesWorkThatFinished_AndPutsTheRestBack_WithoutThrowing()
    {
        using var stopping = new CancellationTokenSource();
        var deliveries = Deliveries(WorkerOutcome.Completed, WorkerOutcome.Completed, WorkerOutcome.Completed);
        _outcomes[2] = async token =>
        {
            await stopping.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return WorkerOutcome.Completed;
        };

        var summary = await Create(maxParallel: 1).ProcessPendingAsync(stopping.Token);

        Assert.Equal(1, summary.Completed);
        await _batch.Received(1).AckAsync(deliveries[0], Arg.Any<CancellationToken>());
        await _batch.Received(1).RequeueAsync(deliveries[1], Arg.Any<CancellationToken>());
        await _batch.Received(1).RequeueAsync(deliveries[2], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTheBrokerIsLostWhileSettling_TheRunEndsQuietly_AndRepeatDeliveriesAreSkippedLater()
    {
        var deliveries = Deliveries(WorkerOutcome.Completed, WorkerOutcome.Completed);
        _batch.AckAsync(deliveries[0], Arg.Any<CancellationToken>()).Returns(Task.FromException(new QueueUnavailableException("gone")));

        var summary = await Create().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(2, summary.Received);
        Assert.Equal(0, summary.Completed);
        await _batch.DidNotReceive().AckAsync(deliveries[1], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheBatchIsAlwaysDisposed_SoUnsettledMessagesReturnToTheQueue()
    {
        Deliveries(WorkerOutcome.Completed);

        await Create().ProcessPendingAsync(CancellationToken.None);

        await _batch.Received(1).DisposeAsync();
    }
}
