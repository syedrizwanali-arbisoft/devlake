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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>A validated, normalized request ready to be logged and queued.</summary>
/// <param name="ClientRefId">The database id of the client making the request.</param>
/// <param name="EventType">One of <see cref="RequestEventTypes"/>.</param>
/// <param name="RequestUrl">The URL pattern recorded in the request log. It never contains an email.</param>
/// <param name="Emails">Normalized, distinct emails.</param>
/// <param name="TimeFrame">The requested time frame.</param>
/// <param name="IdempotencyKey">The key from <see cref="IIdempotencyCheck.ComputeKey"/>.</param>
public sealed record RequestSubmission(
    long ClientRefId, string EventType, string RequestUrl, IReadOnlyList<string> Emails, TimeFrame TimeFrame, string IdempotencyKey);

public enum SubmissionOutcome
{
    /// <summary>Logged and put on the queue.</summary>
    Queued,

    /// <summary>An identical request is already in flight; its correlation ID is returned.</summary>
    Duplicate,

    /// <summary>Logged as revoked because the client has no active callback URL; nothing was queued.</summary>
    NoCallbackUrl
}

public sealed record SubmissionResult(SubmissionOutcome Outcome, string CorrelationId);
