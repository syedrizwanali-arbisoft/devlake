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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

/// <summary>What one run of the processor did, for the log.</summary>
/// <param name="Received">Messages taken off the queue.</param>
/// <param name="Completed">Requests finished (delivered, failed or revoked) and acknowledged.</param>
/// <param name="Requeued">Messages put back to be tried again later.</param>
/// <param name="DeadLettered">Messages that can never be processed and were parked.</param>
/// <param name="Expired">Stale requests marked failed at the start of the run.</param>
/// <param name="HaltReason">Why the run stopped early, or null when it did not.</param>
public sealed record ProcessingRunSummary(int Received, int Completed, int Requeued, int DeadLettered, int Expired, string? HaltReason)
{
    public bool DidWork => Received > 0 || Expired > 0 || HaltReason is not null;
}

public interface IRequestProcessor
{
    /// <summary>
    /// One run: marks stale requests failed, checks the queue, makes sure DevLake and Sophia can be used, then takes a batch off the queue,
    /// processes it with bounded parallelism, and acknowledges, requeues or dead-letters each message. One request's failure never stops the others.
    /// </summary>
    Task<ProcessingRunSummary> ProcessPendingAsync(CancellationToken cancellationToken);
}
