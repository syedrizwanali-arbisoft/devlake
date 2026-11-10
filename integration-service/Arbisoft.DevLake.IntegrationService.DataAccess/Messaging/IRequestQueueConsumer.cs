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

public sealed record QueuedRequest(string CorrelationId, string Json, DateTimeOffset? EnqueuedOn, bool Redelivered);

/// <param name="Processed">Messages whose handler succeeded and which were acknowledged.</param>
/// <param name="Requeued">Messages that failed for the first time and stay queued for the next run.</param>
/// <param name="DeadLettered">Messages that failed again on redelivery and moved to the failed queue.</param>
/// <param name="Halted">The handler stopped the run with a <see cref="QueueProcessingHaltedException"/>.</param>
public sealed record ConsumeResult(int Processed, int Requeued, int DeadLettered, bool Halted = false);

public interface IRequestQueueConsumer
{
    /// <summary>
    /// Takes up to <paramref name="maxMessages"/> queued requests and passes each one to <paramref name="handler"/>.
    /// A message is acknowledged only after its handler returns. Delivery is at least once, so the handler must cope
    /// with seeing a request again, for example after a crash between handling and acknowledging.
    /// </summary>
    Task<ConsumeResult> ConsumeAsync(
        Func<QueuedRequest, CancellationToken, Task> handler,
        int maxMessages,
        CancellationToken cancellationToken = default);
}
