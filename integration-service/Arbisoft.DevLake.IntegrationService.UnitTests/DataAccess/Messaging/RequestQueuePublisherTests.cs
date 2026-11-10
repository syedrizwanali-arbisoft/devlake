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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

public class RequestQueuePublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 30, 0, TimeSpan.Zero);

    private readonly IRabbitMqConnection _connection = Substitute.For<IRabbitMqConnection>();
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly RabbitMqOptions _settings = new() { RequestQueue = "requests", PublishTimeoutSeconds = 10 };
    private BasicProperties? _properties;
    private string? _body;

    public RequestQueuePublisherTests()
    {
        _channel.IsOpen.Returns(true);
        _connection.RequestQueue.Returns("requests");
        _connection.CreateChannelAsync(true, Arg.Any<CancellationToken>()).Returns(_channel);
        _channel.BasicPublishAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Do<BasicProperties>(p => _properties = p),
                Arg.Do<ReadOnlyMemory<byte>>(b => _body = Encoding.UTF8.GetString(b.Span)),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
    }

    private RequestQueuePublisher Create() => new(
        _connection, Options.Create(_settings), new FakeTimeProvider(Now), NullLogger<RequestQueuePublisher>.Instance);

    [Fact]
    public async Task Publish_SendsAPersistentJsonMessageWithTheCorrelationId()
    {
        await Create().PublishAsync("corr-1", "{\"email\":\"a@arbisoft.com\"}");

        await _channel.Received(1).BasicPublishAsync("", "requests", true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        Assert.NotNull(_properties);
        Assert.True(_properties.Persistent);
        Assert.Equal("corr-1", _properties.CorrelationId);
        Assert.Equal("corr-1", _properties.MessageId);
        Assert.Equal("application/json", _properties.ContentType);
        Assert.Equal(Now.ToUnixTimeSeconds(), _properties.Timestamp.UnixTime);
        Assert.Equal("{\"email\":\"a@arbisoft.com\"}", _body);
    }

    [Fact]
    public async Task Publish_ReusesTheChannelWhileItIsOpen()
    {
        var publisher = Create();

        await publisher.PublishAsync("1", "{}");
        await publisher.PublishAsync("2", "{}");

        await _connection.Received(1).CreateChannelAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_OpensANewChannelAfterTheOldOneCloses()
    {
        var publisher = Create();
        await publisher.PublishAsync("1", "{}");

        _channel.IsOpen.Returns(false);
        await publisher.PublishAsync("2", "{}");

        await _connection.Received(2).CreateChannelAsync(true, Arg.Any<CancellationToken>());
        await _channel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Publish_StartsOnAFreshChannelAfterAFailure()
    {
        var publisher = Create();
        _channel.BasicPublishAsync(default(string)!, default(string)!, default, default(BasicProperties)!, default, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("nacked"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync("1", "{}"));
        _channel.BasicPublishAsync(default(string)!, default(string)!, default, default(BasicProperties)!, default, default)
            .ReturnsForAnyArgs(ValueTask.CompletedTask);
        await publisher.PublishAsync("2", "{}");

        await _connection.Received(2).CreateChannelAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_ReportsAMissingConfirmationAsATimeout()
    {
        _settings.PublishTimeoutSeconds = 1;
        _channel.BasicPublishAsync(default(string)!, default(string)!, default, default(BasicProperties)!, default, default)
            .ReturnsForAnyArgs(call => new ValueTask(Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(5))));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Create().PublishAsync("corr-1", "{}"));

        Assert.Contains("corr-1", exception.Message);
    }

    [Fact]
    public async Task Publish_KeepsTheCallersCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().PublishAsync("1", "{}", cancelled.Token));
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData("corr", "")]
    public async Task Publish_RejectsEmptyValues(string correlationId, string json)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Create().PublishAsync(correlationId, json));
    }
}
