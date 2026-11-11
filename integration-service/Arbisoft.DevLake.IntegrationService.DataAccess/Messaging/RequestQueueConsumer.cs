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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>
/// Pulls messages with BasicGet on a channel of its own (INT-Q7), with manual acknowledgement (INT-Q3). A pull consumer has no prefetch,
/// so the per-run limit bounds the work in flight instead (INT-Q4). It reads only schema versions it understands (INT-M3, INT-M4)
/// and moves a message that has been redelivered too often to the dead-letter queue (INT-6).
/// </summary>
public sealed class RequestQueueConsumer : IRequestQueueConsumer
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RequestQueueConsumer> _logger;

    public RequestQueueConsumer(IRabbitMqConnection connection, IOptions<RabbitMqOptions> options, ILogger<RequestQueueConsumer> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IQueueBatch> ReceiveAsync(int maxMessages, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(maxMessages, 1, _options.ReceiveBatchLimit);
        var channel = await _connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
        try
        {
            var deliveries = await ReadAsync(channel, limit, cancellationToken);
            return new QueueBatch(channel, deliveries);
        }
        catch (Exception exception)
        {
            await channel.DisposeAsync();
            throw exception is AlreadyClosedException or OperationInterruptedException or IOException
                ? new QueueUnavailableException("The connection to the broker was lost while reading the queue.", exception)
                : exception;
        }
    }

    private async Task<List<QueueDelivery>> ReadAsync(IChannel channel, int limit, CancellationToken cancellationToken)
    {
        var deliveries = new List<QueueDelivery>(limit);
        while (deliveries.Count < limit)
        {
            var result = await channel.BasicGetAsync(_options.RequestQueueName, autoAck: false, cancellationToken);
            if (result is null)
            {
                break;
            }

            var delivery = TryRead(result);
            if (delivery is null)
            {
                await channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                continue;
            }

            deliveries.Add(delivery);
        }

        return deliveries;
    }

    private QueueDelivery? TryRead(BasicGetResult result)
    {
        var properties = result.BasicProperties;
        var version = ReadInt(properties.Headers, QueueConstants.SchemaVersionHeader);
        if (version is null or < 1 or > QueueConstants.CurrentSchemaVersion)
        {
            _logger.LogWarning("Dead-lettering message {MessageId}: unsupported schema version {Version}", properties.MessageId, version);
            return null;
        }

        if (string.IsNullOrEmpty(properties.CorrelationId) || string.IsNullOrEmpty(properties.Type))
        {
            _logger.LogWarning("Dead-lettering message {MessageId}: missing correlation ID or event type", properties.MessageId);
            return null;
        }

        var earlierDeliveries = ReadInt(properties.Headers, QueueConstants.AcquiredCountHeader) ?? 0;
        if (earlierDeliveries > _options.DeliveryLimit)
        {
            _logger.LogWarning("Dead-lettering message {MessageId}: redelivered {Count} times, over the limit of {Limit}", properties.MessageId, earlierDeliveries, _options.DeliveryLimit);
            return null;
        }

        var message = new QueueMessage(properties.CorrelationId, properties.Type, Encoding.UTF8.GetString(result.Body.Span));
        return new QueueDelivery(message, result.DeliveryTag, result.Redelivered, earlierDeliveries);
    }

    private static int? ReadInt(IDictionary<string, object?>? headers, string name)
    {
        return headers is not null && headers.TryGetValue(name, out var value) && value is int or long or short or byte
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }
}
