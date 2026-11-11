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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>
/// Publishes with broker confirms (INT-Q2) on one channel, one publish at a time because channels are not thread-safe. At the
/// request rates this service accepts, that is far below what a single channel can do.
/// </summary>
public sealed class RequestQueuePublisher : IRequestQueuePublisher, IAsyncDisposable
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private IChannel? _channel;

    public RequestQueuePublisher(IRabbitMqConnection connection, IOptions<RabbitMqOptions> options)
    {
        _connection = connection;
        _options = options.Value;
    }

    public async Task PublishAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            await PublishWithOneRetryAsync(message, cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DropChannelAsync();
        _publishLock.Dispose();
    }

    /// <summary>A channel left open by a broker restart is found closed on first use. Reconnect once before giving up.</summary>
    private async Task PublishWithOneRetryAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await PublishOnceAsync(message, cancellationToken);
        }
        catch (Exception exception) when (IsConnectionLoss(exception))
        {
            await DropChannelAsync();
            try
            {
                await PublishOnceAsync(message, cancellationToken);
            }
            catch (Exception retryException) when (IsConnectionLoss(retryException))
            {
                await DropChannelAsync();
                throw new QueueUnavailableException("The message broker did not accept the request.", retryException);
            }
        }
    }

    private async Task PublishOnceAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken);
        var properties = new BasicProperties
        {
            Persistent = true,
            CorrelationId = message.CorrelationId,
            MessageId = message.CorrelationId,
            Type = message.EventType,
            ContentType = QueueConstants.JsonContentType,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?> { [QueueConstants.SchemaVersionHeader] = QueueConstants.CurrentSchemaVersion }
        };

        try
        {
            await channel.BasicPublishAsync(string.Empty, _options.RequestQueueName, mandatory: true, properties, Encoding.UTF8.GetBytes(message.Json), cancellationToken);
        }
        catch (PublishException exception) when (!exception.IsReturn)
        {
            throw new QueueFullException("The broker rejected the message because the queue is full.", exception);
        }
        catch (PublishException exception)
        {
            throw new QueueUnavailableException("The queue could not be routed to.", exception);
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await DropChannelAsync();
        _channel = await _connection.CreateChannelAsync(publisherConfirms: true, cancellationToken);
        return _channel;
    }

    private async Task DropChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            await _channel.DisposeAsync();
        }
        catch (Exception exception) when (IsConnectionLoss(exception))
        {
            // The channel was already gone; there is nothing left to release.
        }
        finally
        {
            _channel = null;
        }
    }

    private static bool IsConnectionLoss(Exception exception)
    {
        return exception is AlreadyClosedException or OperationInterruptedException or IOException or QueueUnavailableException or TimeoutException;
    }
}
