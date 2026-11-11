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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

public class RequestQueueConsumerTests
{
    private readonly IRabbitMqConnection _connection = Substitute.For<IRabbitMqConnection>();
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly RequestQueueConsumer _consumer;

    public RequestQueueConsumerTests()
    {
        _connection.CreateChannelAsync(false, Arg.Any<CancellationToken>()).Returns(_channel);
        _consumer = new RequestQueueConsumer(_connection, Options.Create(new RabbitMqOptions { RequestQueueName = "q", ReceiveBatchLimit = 3 }), NullLogger<RequestQueueConsumer>.Instance);
    }

    private static BasicGetResult Result(ulong tag, object? schemaVersion = null, string? correlationId = "cid", string? type = "engineer-stats", string body = "{}", bool redelivered = false, long? acquiredCount = null)
    {
        var headers = new Dictionary<string, object?>();
        if (schemaVersion is not null)
        {
            headers[QueueConstants.SchemaVersionHeader] = schemaVersion;
        }

        if (acquiredCount is not null)
        {
            headers[QueueConstants.AcquiredCountHeader] = acquiredCount;
        }

        var properties = new BasicProperties { CorrelationId = correlationId, Type = type, MessageId = "mid", Headers = headers };
        return new BasicGetResult(tag, redelivered, string.Empty, "q", 0, properties, System.Text.Encoding.UTF8.GetBytes(body));
    }

    private void Queue(params BasicGetResult?[] results)
    {
        var sequence = results.Append(null).ToArray();
        _channel.BasicGetAsync("q", false, Arg.Any<CancellationToken>()).Returns(Task.FromResult(sequence[0]), sequence[1..].Select(Task.FromResult).ToArray());
    }

    [Fact]
    public async Task Receive_ReturnsReadableMessagesWithTheirDeliveryDetails()
    {
        Queue(Result(7, schemaVersion: 1, body: "{\"a\":1}", redelivered: true, acquiredCount: 2));

        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        var delivery = Assert.Single(batch.Deliveries);
        Assert.Equal(new QueueMessage("cid", "engineer-stats", "{\"a\":1}"), delivery.Message);
        Assert.Equal(7UL, delivery.DeliveryTag);
        Assert.True(delivery.Redelivered);
        Assert.Equal(2, delivery.DeliveryCount);
    }

    [Fact]
    public async Task Receive_NeverReturnsMoreThanThePerRunLimit()
    {
        Queue(Result(1, 1), Result(2, 1), Result(3, 1), Result(4, 1), Result(5, 1));

        await using var batch = await _consumer.ReceiveAsync(100, CancellationToken.None);

        Assert.Equal(3, batch.Deliveries.Count);
        await _channel.Received(3).BasicGetAsync("q", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_AnEmptyQueue_ReturnsAnEmptyBatch()
    {
        Queue();

        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        Assert.Empty(batch.Deliveries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData("1")]
    public async Task Receive_AnUnsupportedOrMissingSchemaVersion_IsDeadLetteredNotReturned(object? version)
    {
        Queue(Result(9, version));

        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        Assert.Empty(batch.Deliveries);
        await _channel.Received(1).BasicNackAsync(9, false, false, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "engineer-stats")]
    [InlineData("cid", null)]
    public async Task Receive_AMessageWithoutACorrelationIdOrType_IsDeadLettered(string? correlationId, string? type)
    {
        Queue(Result(4, 1, correlationId, type));

        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        Assert.Empty(batch.Deliveries);
        await _channel.Received(1).BasicNackAsync(4, false, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_AMessageDeliveredTooOften_IsDeadLettered_ButOneAtTheLimitIsStillDelivered()
    {
        Queue(Result(1, 1, acquiredCount: 5), Result(2, 1, acquiredCount: 6));

        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        Assert.Equal(1UL, Assert.Single(batch.Deliveries).DeliveryTag);
        await _channel.Received(1).BasicNackAsync(2, false, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_IgnoresUnknownHeadersAndFields_AndStillReadsAnUnknownBodyShape()
    {
        var result = Result(5, 1, body: "{\"email\":\"a@arbisoft.com\",\"somethingNew\":[1,2,3]}");
        result.BasicProperties.Headers!["x-future-header"] = "whatever";
        Queue(result);

        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        Assert.Contains("somethingNew", Assert.Single(batch.Deliveries).Message.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receive_WhenTheConnectionDropsMidRead_ThrowsQueueUnavailable_AndReleasesTheChannel()
    {
        _channel.BasicGetAsync("q", false, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<BasicGetResult?>(new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "gone"))));

        await Assert.ThrowsAsync<QueueUnavailableException>(() => _consumer.ReceiveAsync(10, CancellationToken.None));

        await _channel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Batch_AckRequeueAndDeadLetter_MapToTheMatchingBrokerCalls()
    {
        Queue(Result(1, 1), Result(2, 1), Result(3, 1));
        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);

        await batch.AckAsync(batch.Deliveries[0], CancellationToken.None);
        await batch.RequeueAsync(batch.Deliveries[1], CancellationToken.None);
        await batch.DeadLetterAsync(batch.Deliveries[2], CancellationToken.None);

        await _channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await _channel.Received(1).BasicNackAsync(2, false, true, Arg.Any<CancellationToken>());
        await _channel.Received(1).BasicNackAsync(3, false, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Batch_AckOnALostChannel_ThrowsQueueUnavailable()
    {
        Queue(Result(1, 1));
        await using var batch = await _consumer.ReceiveAsync(10, CancellationToken.None);
        _channel.BasicAckAsync(1, false, Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "gone"))));

        await Assert.ThrowsAsync<QueueUnavailableException>(() => batch.AckAsync(batch.Deliveries[0], CancellationToken.None));
    }
}
