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

using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>
/// Keeps one connection to the broker and replaces it when it is found closed, for example after a broker restart.
/// It connects on first use, so the service starts even when the broker is down and reports it through readiness instead.
/// </summary>
public sealed class RabbitMqConnection : IRabbitMqConnection
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IChannel> CreateChannelAsync(bool publisherConfirms, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await GetOpenConnectionAsync(cancellationToken);
            var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: publisherConfirms, publisherConfirmationTrackingEnabled: publisherConfirms);
            return await connection.CreateChannelAsync(channelOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is BrokerUnreachableException or AlreadyClosedException or OperationInterruptedException or IOException or System.Net.Sockets.SocketException)
        {
            throw new QueueUnavailableException("The message broker is not reachable.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connectLock.WaitAsync();
        try
        {
            await CloseAsync();
        }
        finally
        {
            _connectLock.Release();
            _connectLock.Dispose();
        }
    }

    private async Task<IConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            await CloseAsync();
            _connection = await ConnectAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<IConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ConnectionName,
            RequestedConnectionTimeout = ConnectTimeout,
            AutomaticRecoveryEnabled = false,
            Ssl = new SslOption { Enabled = _options.UseTls, ServerName = _options.HostName }
        };

        var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            await RabbitMqTopology.DeclareAsync(channel, _options, cancellationToken);
        }

        _logger.LogInformation("Connected to RabbitMQ and declared queue {Queue}", _options.RequestQueueName);
        return connection;
    }

    private async Task CloseAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            if (_connection.IsOpen)
            {
                await _connection.CloseAsync();
            }
        }
        catch (Exception exception) when (exception is AlreadyClosedException or IOException)
        {
            _logger.LogDebug(exception, "The RabbitMQ connection was already closed");
        }
        finally
        {
            _connection.Dispose();
            _connection = null;
        }
    }
}
