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

using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

public sealed class RequestQueuePublisherTests : IAsyncDisposable
{
    private static readonly QueueMessage Message = new("0b8f3c52-0000-4000-8000-000000000001", "engineer-stats", "{\"email\":\"a@arbisoft.com\"}");

    private readonly IRabbitMqConnection _connection = Substitute.For<IRabbitMqConnection>();
    private readonly RequestQueuePublisher _publisher;

    public RequestQueuePublisherTests()
    {
        _publisher = new RequestQueuePublisher(_connection, Options.Create(new RabbitMqOptions { RequestQueueName = "q" }));
    }

    public async ValueTask DisposeAsync()
    {
        await _publisher.DisposeAsync();
    }

    private static IChannel OpenChannel()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        return channel;
    }

    private static void PublishThrows(IChannel channel, Exception exception)
    {
        channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(exception));
    }

    private static AlreadyClosedException Closed() => new(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "broker restarted"));

    [Fact]
    public async Task Publish_SendsAPersistentMessageToTheQueue_WithTheCorrelationIdAndSchemaVersion()
    {
        var channel = OpenChannel();
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns(channel);
        BasicProperties? sent = null;
        await channel.BasicPublishAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Do<BasicProperties>(p => sent = p), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());

        await _publisher.PublishAsync(Message, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.True(sent.Persistent);
        Assert.Equal(Message.CorrelationId, sent.CorrelationId);
        Assert.Equal("engineer-stats", sent.Type);
        Assert.Equal(QueueConstants.JsonContentType, sent.ContentType);
        Assert.Equal(QueueConstants.CurrentSchemaVersion, sent.Headers![QueueConstants.SchemaVersionHeader]);
        await channel.Received(1).BasicPublishAsync(string.Empty, "q", true, Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_ReusesTheSameChannel_ForLaterMessages()
    {
        var channel = OpenChannel();
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns(channel);

        await _publisher.PublishAsync(Message, CancellationToken.None);
        await _publisher.PublishAsync(Message, CancellationToken.None);

        await _connection.Received(1).CreateChannelAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_AfterTheChannelWasClosedByABrokerRestart_ReconnectsOnceAndSucceeds()
    {
        var stale = OpenChannel();
        PublishThrows(stale, Closed());
        var fresh = OpenChannel();
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns(stale, fresh);

        await _publisher.PublishAsync(Message, CancellationToken.None);

        await _connection.Received(2).CreateChannelAsync(true, Arg.Any<CancellationToken>());
        await fresh.Received(1).BasicPublishAsync(string.Empty, "q", true, Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WhenTheBrokerStaysUnreachable_ThrowsQueueUnavailable()
    {
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns<IChannel>(_ => throw new QueueUnavailableException("down"));

        await Assert.ThrowsAsync<QueueUnavailableException>(() => _publisher.PublishAsync(Message, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_RejectedByTheBroker_ThrowsQueueFull()
    {
        var channel = OpenChannel();
        PublishThrows(channel, new PublishException(1, isReturn: false));
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns(channel);

        await Assert.ThrowsAsync<QueueFullException>(() => _publisher.PublishAsync(Message, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_ReturnedAsUnroutable_ThrowsQueueUnavailable()
    {
        var channel = OpenChannel();
        PublishThrows(channel, new PublishException(1, isReturn: true));
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns(channel);

        await Assert.ThrowsAsync<QueueUnavailableException>(() => _publisher.PublishAsync(Message, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_Cancelled_ThrowsOperationCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _publisher.PublishAsync(Message, cancellation.Token));
    }
}
