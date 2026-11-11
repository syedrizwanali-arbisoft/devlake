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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public interface IIdempotencyCheck
{
    /// <summary>
    /// The idempotency key for a request: the same client, event type, period and set of emails always give the same key,
    /// whatever the order of the emails. The emails must already be normalized (see <see cref="EmailNormalizer"/>).
    /// </summary>
    string ComputeKey(long clientRefId, string eventType, StatsPeriod period, IReadOnlyCollection<string> normalizedEmails);

    /// <summary>The earlier request that makes this one a duplicate, or null when it should be queued.</summary>
    Task<RequestLog?> FindDuplicateAsync(string key, CancellationToken cancellationToken);
}
