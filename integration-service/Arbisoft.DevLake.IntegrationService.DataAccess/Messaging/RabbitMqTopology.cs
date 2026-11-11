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
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>
/// The queues and exchange the service needs, declared by the broker, not improvised in application code (INT-Q6):
/// a durable quorum queue with a length cap, a TTL and a dead-letter exchange, plus the dead-letter queue. The redelivery limit is
/// enforced by the consumer: a pull consumer (BasicGet) is not subject to the broker's own x-delivery-limit.
/// </summary>
public static class RabbitMqTopology
{
    private const string QuorumQueueType = "quorum";
    private const string RejectPublishOverflow = "reject-publish";

    public static string DeadLetterExchange(RabbitMqOptions options) => options.RequestQueueName + QueueConstants.DeadLetterExchangeSuffix;

    public static string DeadLetterQueue(RabbitMqOptions options) => options.RequestQueueName + QueueConstants.DeadLetterQueueSuffix;

    /// <remarks>
    /// A quorum queue with reject-publish applies its limit one message late: filling it accepts x-max-length + 1 messages, then it
    /// rejects until the length drops below x-max-length. The argument is therefore one less than the configured maximum, so the queue
    /// fills to exactly <see cref="RabbitMqOptions.QueueMaxLength"/> messages. Room returns once it falls one message under that.
    /// </remarks>
    public static Dictionary<string, object?> RequestQueueArguments(RabbitMqOptions options)
    {
        return new Dictionary<string, object?>
        {
            ["x-queue-type"] = QuorumQueueType,
            ["x-max-length"] = options.QueueMaxLength - 1,
            ["x-overflow"] = RejectPublishOverflow,
            ["x-message-ttl"] = options.MessageTtlSeconds * 1000,
            ["x-dead-letter-exchange"] = DeadLetterExchange(options)
        };
    }

    /// <summary>Declares everything. Declaring is idempotent as long as the arguments match what already exists.</summary>
    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options, CancellationToken cancellationToken)
    {
        var deadExchange = DeadLetterExchange(options);
        var deadQueue = DeadLetterQueue(options);

        await channel.ExchangeDeclareAsync(deadExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(deadQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(deadQueue, deadExchange, routingKey: string.Empty, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            options.RequestQueueName, durable: true, exclusive: false, autoDelete: false, RequestQueueArguments(options), cancellationToken: cancellationToken);
    }
}
