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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

public sealed class RequestQueueConsumer(IRabbitMqConnection connection, ILogger<RequestQueueConsumer> logger)
    : IRequestQueueConsumer
{
    public async Task<ConsumeResult> ConsumeAsync(
        Func<QueuedRequest, CancellationToken, Task> handler,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessages);

        int processed = 0, requeued = 0, deadLettered = 0;
        var halted = false;

        // Messages left unacknowledged go back to the queue when this channel closes, including after a crash or
        // cancellation. Holding them until then means a failed message isn't fetched again within the same run.
        await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);

        while (processed + requeued + deadLettered < maxMessages)
        {
            var message = await channel.BasicGetAsync(connection.RequestQueue, autoAck: false, cancellationToken);
            if (message is null)
            {
                break;
            }

            var properties = message.BasicProperties;
            var request = new QueuedRequest(
                properties.CorrelationId ?? string.Empty,
                Encoding.UTF8.GetString(message.Body.Span),
                properties.IsTimestampPresent() ? DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime) : null,
                message.Redelivered);

            try
            {
                await handler(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (QueueProcessingHaltedException ex)
            {
                // The message stays unacknowledged, so it goes back to the queue when the channel closes.
                logger.LogWarning(ex, "Processing stopped at request {CorrelationId}; it stays queued.", request.CorrelationId);
                halted = true;
                break;
            }
            catch (Exception ex)
            {
                if (message.Redelivered)
                {
                    logger.LogError(ex, "Request {CorrelationId} failed again and was moved to the failed queue.", request.CorrelationId);
                    await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                    deadLettered++;
                }
                else
                {
                    logger.LogWarning(ex, "Request {CorrelationId} failed and will be retried on the next run.", request.CorrelationId);
                    requeued++;
                }

                continue;
            }

            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken);
            processed++;
        }

        return new ConsumeResult(processed, requeued, deadLettered, halted);
    }
}
