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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

public interface IRequestQueueConsumer
{
    /// <summary>
    /// Takes up to <paramref name="maxMessages"/> requests off the queue (never more than the configured per-run limit) without
    /// acknowledging them. Messages the code cannot read go to the dead-letter queue instead of being returned.
    /// </summary>
    /// <exception cref="QueueUnavailableException">The broker could not be reached.</exception>
    Task<IQueueBatch> ReceiveAsync(int maxMessages, CancellationToken cancellationToken);

    /// <summary>
    /// How many messages are waiting in the queue. A cheap check that does not take any message, so it does not raise a message's delivery count.
    /// Also the queue-depth figure to publish as a metric.
    /// </summary>
    /// <exception cref="QueueUnavailableException">The broker could not be reached.</exception>
    Task<int> GetDepthAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A set of received, not yet acknowledged messages and the channel they came from. Acknowledge each message only after its work has
/// committed. Disposing the batch returns every message that was not acknowledged to the queue, so nothing is lost.
/// </summary>
public interface IQueueBatch : IAsyncDisposable
{
    IReadOnlyList<QueueDelivery> Deliveries { get; }

    /// <summary>The work is done: remove the message from the queue.</summary>
    Task AckAsync(QueueDelivery delivery, CancellationToken cancellationToken);

    /// <summary>The work could not be done now: put the message back so it is delivered again.</summary>
    Task RequeueAsync(QueueDelivery delivery, CancellationToken cancellationToken);

    /// <summary>The message can never succeed: move it to the dead-letter queue.</summary>
    Task DeadLetterAsync(QueueDelivery delivery, CancellationToken cancellationToken);
}
