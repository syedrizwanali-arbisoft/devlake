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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

public sealed class RequestQueuePublisher(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RequestQueuePublisher> logger) : IRequestQueuePublisher, IAsyncDisposable
{
    private const string JsonContentType = "application/json";

    // A channel must not publish from two threads at once, so publishes share one channel under this lock.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(string correlationId, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var timeout = TimeSpan.FromSeconds(options.Value.PublishTimeoutSeconds);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                await ResetChannelAsync();
                _channel = await connection.CreateChannelAsync(publisherConfirms: true, timeoutSource.Token);
            }

            var properties = new BasicProperties
            {
                Persistent = true,
                CorrelationId = correlationId,
                MessageId = correlationId,
                ContentType = JsonContentType,
                Timestamp = new AmqpTimestamp(timeProvider.GetUtcNow().ToUnixTimeSeconds()),
            };

            // Mandatory, so a message the broker can't route raises an error instead of being dropped.
            await _channel.BasicPublishAsync(string.Empty, connection.RequestQueue, mandatory: true, properties,
                Encoding.UTF8.GetBytes(json), timeoutSource.Token);
        }
        catch (Exception ex)
        {
            // Whether the broker kept the message is unknown, so start the next publish on a fresh channel.
            await ResetChannelAsync();
            logger.LogError(ex, "Publishing request {CorrelationId} to RabbitMQ failed.", correlationId);

            if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"RabbitMQ didn't confirm request {correlationId} within {timeout.TotalSeconds:0} seconds.", ex);
            }

            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _lock.Dispose();
    }

    private async Task ResetChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Disposing a RabbitMQ channel failed.");
        }
    }
}
