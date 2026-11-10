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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

public class RabbitMqConnectionTests
{
    private static readonly RabbitMqOptions Settings = new()
    {
        Host = "rabbit", Port = 5673, VirtualHost = "/vh", Username = "user", Password = "pass", RequestQueue = "requests",
    };

    private readonly IConnectionFactory _factory = Substitute.For<IConnectionFactory>();
    private readonly RabbitMqConnection _connection;

    public RabbitMqConnectionTests()
    {
        _connection = new RabbitMqConnection(_factory, Options.Create(Settings), NullLogger<RabbitMqConnection>.Instance);
    }

    private static IConnection OpenConnection(IChannel channel)
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);
        return connection;
    }

    [Fact]
    public void CreateFactory_UsesTheSettingsAndLeavesRecoveryToThisClass()
    {
        var factory = RabbitMqConnection.CreateFactory(Settings);

        Assert.Equal("rabbit", factory.HostName);
        Assert.Equal(5673, factory.Port);
        Assert.Equal("/vh", factory.VirtualHost);
        Assert.Equal("user", factory.UserName);
        Assert.Equal("pass", factory.Password);
        Assert.False(factory.AutomaticRecoveryEnabled);
        Assert.False(factory.TopologyRecoveryEnabled);
    }

    [Theory]
    [InlineData("Host", "RabbitMq:Host")]
    [InlineData("Username", "RabbitMq__Username")]
    [InlineData("Password", "RabbitMq__Password")]
    [InlineData("RequestQueue", "RabbitMq:RequestQueue")]
    public void CreateFactory_NamesAMissingSetting(string property, string expected)
    {
        var settings = new RabbitMqOptions
        {
            Host = "h", Username = "u", Password = "p", RequestQueue = "q",
        };
        typeof(RabbitMqOptions).GetProperty(property)!.SetValue(settings, "");

        var exception = Assert.Throws<InvalidOperationException>(() => RabbitMqConnection.CreateFactory(settings));
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public async Task CreateChannel_DeclaresDurableQueuesWithDeadLettering()
    {
        var channel = Substitute.For<IChannel>();
        var openConnection = OpenConnection(channel);
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(openConnection);

        Assert.Same(channel, await _connection.CreateChannelAsync(publisherConfirms: true));

        await channel.Received(1).QueueDeclareAsync("requests.failed", Arg.Is(true), Arg.Is(false), Arg.Is(false),
            Arg.Is<IDictionary<string, object?>?>(a => a == null), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await channel.Received(1).QueueDeclareAsync("requests", Arg.Is(true), Arg.Is(false), Arg.Is(false),
            Arg.Is<IDictionary<string, object?>?>(a =>
                a != null && (string)a["x-dead-letter-exchange"]! == "" && (string)a["x-dead-letter-routing-key"]! == "requests.failed"),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateChannel_TurnsOnPublisherConfirmsWhenAsked()
    {
        var connection = OpenConnection(Substitute.For<IChannel>());
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        await _connection.CreateChannelAsync(publisherConfirms: true);
        await _connection.CreateChannelAsync(publisherConfirms: false);

        await connection.Received(1).CreateChannelAsync(
            Arg.Is<CreateChannelOptions?>(o => o!.PublisherConfirmationsEnabled && o.PublisherConfirmationTrackingEnabled),
            Arg.Any<CancellationToken>());
        await connection.Received(1).CreateChannelAsync(
            Arg.Is<CreateChannelOptions?>(o => !o!.PublisherConfirmationsEnabled), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateChannel_ReusesAnOpenConnection()
    {
        var openConnection = OpenConnection(Substitute.For<IChannel>());
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(openConnection);

        await _connection.CreateChannelAsync(false);
        await _connection.CreateChannelAsync(false);

        await _factory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateChannel_ReconnectsAfterTheConnectionCloses()
    {
        var first = OpenConnection(Substitute.For<IChannel>());
        var second = OpenConnection(Substitute.For<IChannel>());
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(first, second);

        await _connection.CreateChannelAsync(false);
        first.IsOpen.Returns(false);
        await _connection.CreateChannelAsync(false);

        await _factory.Received(2).CreateConnectionAsync(Arg.Any<CancellationToken>());
        await first.Received(1).DisposeAsync();
        await second.Received(1).CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateChannel_RetriesTheConnectionAfterAFailedAttempt()
    {
        var connection = OpenConnection(Substitute.For<IChannel>());
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("broker down"), _ => Task.FromResult(connection));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _connection.CreateChannelAsync(false));
        await _connection.CreateChannelAsync(false);

        await _factory.Received(2).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateChannel_DisposesTheChannelWhenDeclaringFails()
    {
        var channel = Substitute.For<IChannel>();
        channel.QueueDeclareAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("precondition"));
        var openConnection = OpenConnection(channel);
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(openConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _connection.CreateChannelAsync(false));

        await channel.Received(1).DisposeAsync();
    }
}
