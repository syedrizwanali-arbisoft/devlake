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

public static class ProcessingConstants
{
    /// <summary>Recorded as <c>attemptBy</c> on every attempt the processing job makes.</summary>
    public const string AttemptBy = "RequestProcessor";

    public const string EmptyJson = "{}";
}

/// <summary>The values stored in RequestResponseAttempts.attemptStatus.</summary>
public static class DeliveryAttemptStatus
{
    /// <summary>The client's callback accepted the result.</summary>
    public const string Delivered = "Delivered";

    /// <summary>A try to deliver the result failed. These count towards the client's maxAttempts.</summary>
    public const string DeliveryFailed = "DeliveryFailed";

    /// <summary>The stats could not be collected, so nothing was delivered. This ends the request.</summary>
    public const string FetchFailed = "FetchFailed";

    /// <summary>The request waited in the queue until it expired.</summary>
    public const string Expired = "Expired";
}

/// <summary>What happens to a queued message once the job has dealt with it.</summary>
public enum WorkerOutcome
{
    /// <summary>The request is finished, whatever the result: acknowledge the message.</summary>
    Completed,

    /// <summary>Put the message back: the request cannot be dealt with yet.</summary>
    Requeue,

    /// <summary>Put the message back and stop taking work: something systemic (credentials, DevLake or Sophia down) stands in the way of every request.</summary>
    StopRun,

    /// <summary>The message can never be processed: move it to the dead-letter queue.</summary>
    DeadLetter
}
