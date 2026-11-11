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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public sealed class RequestLogRepository : RepositoryBase, IRequestLogRepository
{
    private const string Columns = "id, clientRefId, correlationId, url, status, eventType, createdOn, idempotencyKey";
    private const string IdempotencyIndex = "UX_RequestLogs_idempotencyKey";
    private const string AttemptColumns = "id, requestLogId, responseJson, attemptStatus, failureError, attemptBy, attemptOn";

    public RequestLogRepository(IDbSession session, IOptions<DatabaseOptions> options)
        : base(session, options)
    {
    }

    public Task<long> InsertAsync(RequestLog requestLog, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO RequestLogs (clientRefId, correlationId, url, status, eventType, createdOn, idempotencyKey) " +
            "VALUES (@ClientRefId, @CorrelationId, @Url, @Status, @EventType, @CreatedOn, @IdempotencyKey)",
            requestLog,
            cancellationToken);
    }

    public async Task<long?> TryInsertAsync(RequestLog requestLog, CancellationToken cancellationToken)
    {
        try
        {
            return await InsertAsync(requestLog, cancellationToken);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry && exception.Message.Contains(IdempotencyIndex, StringComparison.Ordinal))
        {
            return null;
        }
    }

    public Task<RequestLog?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<RequestLog>($"SELECT {Columns} FROM RequestLogs WHERE idempotencyKey = @idempotencyKey", new { idempotencyKey }, cancellationToken);
    }

    public async Task ReleaseIdempotencyKeyAsync(long requestLogId, CancellationToken cancellationToken)
    {
        await ExecuteAsync("UPDATE RequestLogs SET idempotencyKey = NULL WHERE id = @requestLogId", new { requestLogId }, cancellationToken);
    }

    public Task<long> InsertJsonAsync(RequestLogJson json, CancellationToken cancellationToken)
    {
        return InsertAsync("INSERT INTO RequestLogJsons (requestLogId, json) VALUES (@RequestLogId, @Json)", json, cancellationToken);
    }

    public Task<RequestLog?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<RequestLog>($"SELECT {Columns} FROM RequestLogs WHERE correlationId = @correlationId", new { correlationId }, cancellationToken);
    }

    public Task<RequestLog?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<RequestLog>($"SELECT {Columns} FROM RequestLogs WHERE id = @id", new { id }, cancellationToken);
    }

    public Task<string?> GetJsonAsync(long requestLogId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string>("SELECT json FROM RequestLogJsons WHERE requestLogId = @requestLogId ORDER BY id LIMIT 1", new { requestLogId }, cancellationToken);
    }

    public async Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken)
    {
        var changed = await ExecuteAsync("UPDATE RequestLogs SET status = @status WHERE id = @id", new { id, status }, cancellationToken);
        return changed > 0;
    }

    public Task<IReadOnlyList<RequestLog>> ListByStatusSinceAsync(string status, DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        return QueryAsync<RequestLog>(
            $"SELECT {Columns} FROM RequestLogs WHERE status = @status AND createdOn >= @since ORDER BY createdOn, id LIMIT @limit",
            new { status, since, limit },
            cancellationToken);
    }

    public Task<IReadOnlyList<RequestLog>> ListStaleAsync(IReadOnlyCollection<string> statuses, DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken)
    {
        return QueryAsync<RequestLog>(
            $"SELECT {Columns} FROM RequestLogs WHERE status IN @statuses AND createdOn < @createdBefore ORDER BY createdOn, id LIMIT @limit",
            new { statuses, createdBefore, limit },
            cancellationToken);
    }

    public Task<IReadOnlyList<long>> GetClientsWithPendingRequestsAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<long>(
            "SELECT DISTINCT clientRefId FROM RequestLogs WHERE status IN @statuses ORDER BY clientRefId",
            new { statuses = new[] { RequestStatus.Queued, RequestStatus.Processing } },
            cancellationToken);
    }

    public Task<PagedResult<RequestLog>> ListAsync(RequestLogFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        const string fromAndWhere =
            "FROM RequestLogs WHERE (@ClientRefId IS NULL OR clientRefId = @ClientRefId) AND (@Status IS NULL OR status = @Status) " +
            "AND (@From IS NULL OR createdOn >= @From) AND (@To IS NULL OR createdOn < @To)";
        return QueryPageAsync<RequestLog>(Columns, fromAndWhere, "createdOn DESC, id DESC", filter, page, pageSize, cancellationToken);
    }

    public Task<long> InsertAttemptAsync(RequestResponseAttempt attempt, CancellationToken cancellationToken)
    {
        return InsertAsync(
            "INSERT INTO RequestResponseAttempts (requestLogId, responseJson, attemptStatus, failureError, attemptBy, attemptOn) " +
            "VALUES (@RequestLogId, @ResponseJson, @AttemptStatus, @FailureError, @AttemptBy, @AttemptOn)",
            attempt,
            cancellationToken);
    }

    public Task<IReadOnlyList<RequestResponseAttempt>> GetAttemptsAsync(long requestLogId, CancellationToken cancellationToken)
    {
        return QueryAsync<RequestResponseAttempt>($"SELECT {AttemptColumns} FROM RequestResponseAttempts WHERE requestLogId = @requestLogId ORDER BY attemptOn, id", new { requestLogId }, cancellationToken);
    }
}
