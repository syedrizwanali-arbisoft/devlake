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

using System.Text;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

public class RequestQueueConsumerTests
{
    private readonly IRabbitMqConnection _connection = Substitute.For<IRabbitMqConnection>();
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly Queue<BasicGetResult> _queue = new();
    private readonly RequestQueueConsumer _consumer;

    public RequestQueueConsumerTests()
    {
        _connection.RequestQueue.Returns("requests");
        _connection.CreateChannelAsync(false, Arg.Any<CancellationToken>()).Returns(_channel);
        _channel.BasicGetAsync("requests", false, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_queue.TryDequeue(out var message) ? message : null));
        _consumer = new RequestQueueConsumer(_connection, NullLogger<RequestQueueConsumer>.Instance);
    }

    private void Enqueue(ulong tag, string correlationId, bool redelivered = false, long? timestamp = 1_793_000_000)
    {
        var properties = new BasicProperties { CorrelationId = correlationId };
        if (timestamp is not null)
        {
            properties.Timestamp = new AmqpTimestamp(timestamp.Value);
        }

        _queue.Enqueue(new BasicGetResult(tag, redelivered, "", "requests", 0, properties,
            Encoding.UTF8.GetBytes("{\"id\":\"" + correlationId + "\"}")));
    }

    [Fact]
    public async Task Consume_PassesEachMessageToTheHandlerAndAcksIt()
    {
        Enqueue(1, "a", timestamp: 1_793_000_000);
        Enqueue(2, "b", redelivered: true, timestamp: null);
        var seen = new List<QueuedRequest>();

        var result = await _consumer.ConsumeAsync((request, _) => { seen.Add(request); return Task.CompletedTask; }, 10);

        Assert.Equal(new ConsumeResult(2, 0, 0), result);
        Assert.Equal(new QueuedRequest("a", "{\"id\":\"a\"}", DateTimeOffset.FromUnixTimeSeconds(1_793_000_000), false), seen[0]);
        Assert.Equal(new QueuedRequest("b", "{\"id\":\"b\"}", null, true), seen[1]);
        await _channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await _channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await _channel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Consume_AcksOnlyAfterTheHandlerFinishes()
    {
        Enqueue(1, "a");
        var ackedDuringHandler = true;

        await _consumer.ConsumeAsync((_, _) =>
        {
            ackedDuringHandler = _channel.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IChannel.BasicAckAsync));
            return Task.CompletedTask;
        }, 10);

        Assert.False(ackedDuringHandler);
        await _channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_StopsAtMaxMessages()
    {
        Enqueue(1, "a");
        Enqueue(2, "b");
        Enqueue(3, "c");

        var result = await _consumer.ConsumeAsync((_, _) => Task.CompletedTask, 2);

        Assert.Equal(new ConsumeResult(2, 0, 0), result);
        Assert.Single(_queue);
    }

    [Fact]
    public async Task Consume_LeavesAFirstFailureUnackedAndCarriesOn()
    {
        Enqueue(1, "fails");
        Enqueue(2, "works");

        var result = await _consumer.ConsumeAsync(
            (request, _) => request.CorrelationId == "fails" ? throw new InvalidOperationException("boom") : Task.CompletedTask, 10);

        Assert.Equal(new ConsumeResult(1, 1, 0), result);
        await _channel.DidNotReceive().BasicAckAsync(1, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _channel.DidNotReceive().BasicNackAsync(1, Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await _channel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Consume_DeadLettersAMessageThatFailsAgainOnRedelivery()
    {
        Enqueue(1, "fails", redelivered: true);

        var result = await _consumer.ConsumeAsync((_, _) => throw new InvalidOperationException("boom"), 10);

        Assert.Equal(new ConsumeResult(0, 0, 1), result);
        await _channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_LeavesTheMessageQueuedWhenCancelled()
    {
        Enqueue(1, "a");
        using var stop = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _consumer.ConsumeAsync(async (_, token) =>
        {
            await stop.CancelAsync();
            token.ThrowIfCancellationRequested();
        }, 10, stop.Token));

        await _channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, default);
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
        await _channel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Consume_StopsAndLeavesTheMessageQueuedWhenTheHandlerHalts()
    {
        Enqueue(1, "a");
        Enqueue(2, "halts");
        Enqueue(3, "never-reached");

        var result = await _consumer.ConsumeAsync((request, _) => request.CorrelationId == "halts"
            ? throw new QueueProcessingHaltedException("token rejected")
            : Task.CompletedTask, 10);

        Assert.Equal(new ConsumeResult(1, 0, 0, Halted: true), result);
        await _channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceive().BasicAckAsync(2, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
        Assert.Single(_queue);
    }

    [Fact]
    public async Task Consume_DoesNotDeadLetterARedeliveredMessageThatHalts()
    {
        Enqueue(1, "a", redelivered: true);

        var result = await _consumer.ConsumeAsync((_, _) => throw new QueueProcessingHaltedException("down"), 10);

        Assert.True(result.Halted);
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task Consume_ReturnsNothingForAnEmptyQueue()
    {
        Assert.Equal(new ConsumeResult(0, 0, 0), await _consumer.ConsumeAsync((_, _) => Task.CompletedTask, 10));
    }

    [Fact]
    public async Task Consume_RejectsANonPositiveLimit()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _consumer.ConsumeAsync((_, _) => Task.CompletedTask, 0));
    }
}
