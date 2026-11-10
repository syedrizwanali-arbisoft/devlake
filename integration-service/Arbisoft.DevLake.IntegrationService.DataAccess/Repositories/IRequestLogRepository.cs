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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public interface IRequestLogRepository
{
    /// <summary>Saves the log and its JSON body in one transaction.</summary>
    Task<long> AddAsync(RequestLog log, string json, CancellationToken cancellationToken = default);
    /// <summary>
    /// Returns the newest request from the client with the same hash created at or after <paramref name="since"/>,
    /// ignoring revoked requests, which the client may send again.
    /// </summary>
    Task<RequestLog?> FindRecentDuplicateAsync(long clientRefId, string requestHash, DateTime since, CancellationToken cancellationToken = default);
    Task<RequestLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Returns one page of logs, newest first, and the number of logs that match. Null filters match everything.</summary>
    Task<(IReadOnlyList<RequestLog> Logs, int Total)> SearchAsync(
        long? clientRefId, string? status, DateTime? from, DateTime? to, int limit, int offset, CancellationToken cancellationToken = default);

    Task<RequestLog?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<string?> GetJsonAsync(long requestLogId, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default);
    Task<long> AddAttemptAsync(RequestResponseAttempt attempt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RequestResponseAttempt>> GetAttemptsAsync(long requestLogId, CancellationToken cancellationToken = default);
}
