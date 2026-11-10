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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed record StatsSubmission(
    long ClientRefId,
    string ClientId,
    string EventType,
    IReadOnlyList<string> Emails,
    TimeFrame TimeFrame,
    string RequestHash,
    string Url);

public interface IRequestService
{
    /// <summary>Logs the request and queues it, or logs it as revoked when the client has no active callback.</summary>
    Task<StatsSubmissionResult> QueueAsync(StatsSubmission submission, CancellationToken cancellationToken = default);
}
