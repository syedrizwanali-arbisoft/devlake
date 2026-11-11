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

/// <summary>The request log aggregate: the log row, its JSON body and its delivery attempts.</summary>
public interface IRequestLogRepository
{
    Task<long> InsertAsync(RequestLog requestLog, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a request that carries an idempotency key. Returns null, and inserts nothing, when another request already holds that key,
    /// which is how the database stops two identical requests racing past an application-level check.
    /// </summary>
    Task<long?> TryInsertAsync(RequestLog requestLog, CancellationToken cancellationToken);

    /// <summary>The request that currently holds an idempotency key, or null.</summary>
    Task<RequestLog?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Frees the idempotency key held by one specific request so an identical request can be queued again. It targets the row, not the key,
    /// so it can never clear a newer request that has since taken the key.
    /// </summary>
    Task ReleaseIdempotencyKeyAsync(long requestLogId, CancellationToken cancellationToken);

    Task<long> InsertJsonAsync(RequestLogJson json, CancellationToken cancellationToken);

    Task<RequestLog?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken);

    Task<RequestLog?> GetByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>The JSON body of a request log, or null when it has none.</summary>
    Task<string?> GetJsonAsync(long requestLogId, CancellationToken cancellationToken);

    Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken);

    /// <summary>Requests with the given status created at or after <paramref name="since"/>, oldest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<RequestLog>> ListByStatusSinceAsync(string status, DateTimeOffset since, int limit, CancellationToken cancellationToken);

    Task<PagedResult<RequestLog>> ListAsync(RequestLogFilter filter, int page, int pageSize, CancellationToken cancellationToken);

    Task<long> InsertAttemptAsync(RequestResponseAttempt attempt, CancellationToken cancellationToken);

    Task<IReadOnlyList<RequestResponseAttempt>> GetAttemptsAsync(long requestLogId, CancellationToken cancellationToken);
}
