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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>
/// Holds one connection to the broker. The client's automatic recovery is off; a closed connection, for example after a
/// broker restart, is replaced the next time a channel is needed. Nothing consumes continuously, so lazy reconnection
/// is enough, and it never leaves a half-recovered channel behind.
/// </summary>
public sealed class RabbitMqConnection(
    IConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnection> logger) : IRabbitMqConnection, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public string RequestQueue => options.Value.RequestQueue;

    public static ConnectionFactory CreateFactory(RabbitMqOptions options)
    {
        Require(options.Host, "RabbitMq:Host");
        Require(options.Username, "RabbitMq:Username (the RabbitMq__Username environment variable)");
        Require(options.Password, "RabbitMq:Password (the RabbitMq__Password environment variable)");
        Require(options.RequestQueue, "RabbitMq:RequestQueue");

        return new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.Username,
            Password = options.Password,
            ClientProvidedName = "integration-service",
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
        };
    }

    public async Task<IChannel> CreateChannelAsync(bool publisherConfirms, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirms, publisherConfirmationTrackingEnabled: publisherConfirms),
            cancellationToken);
        try
        {
            await DeclareQueuesAsync(channel, cancellationToken);
            return channel;
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } current)
        {
            return current;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                logger.LogWarning("The RabbitMQ connection was closed. Reconnecting.");
                await _connection.DisposeAsync();
                _connection = null;
            }

            _connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Declaring is idempotent, so doing it on every channel also restores the queues if the broker lost them.
    private async Task DeclareQueuesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await channel.QueueDeclareAsync(settings.FailedQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(settings.RequestQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = settings.FailedQueue,
            },
            cancellationToken: cancellationToken);
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is not set.");
        }
    }
}
