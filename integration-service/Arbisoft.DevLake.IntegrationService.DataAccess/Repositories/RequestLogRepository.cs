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

using Dapper;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

public sealed class RequestLogRepository(IDbConnectionFactory connectionFactory) : IRequestLogRepository
{
    public async Task<long> AddAsync(RequestLog log, string json, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO RequestLogs (clientRefId, correlationId, url, status, eventType, createdOn)
            VALUES (@ClientRefId, @CorrelationId, @Url, @Status, @EventType, @CreatedOn);
            SELECT LAST_INSERT_ID();
            """, log, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO RequestLogJsons (requestLogId, `json`) VALUES (@id, @json)",
            new { id, json }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<RequestLog?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RequestLog>(new CommandDefinition(
            "SELECT * FROM RequestLogs WHERE correlationId = @correlationId",
            new { correlationId }, cancellationToken: cancellationToken));
    }

    public async Task<string?> GetJsonAsync(long requestLogId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT `json` FROM RequestLogJsons WHERE requestLogId = @requestLogId",
            new { requestLogId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE RequestLogs SET status = @status WHERE id = @id",
            new { id, status }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<long> AddAttemptAsync(RequestResponseAttempt attempt, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO RequestResponseAttempts (requestLogId, responseJson, attemptStatus, failureError, attemptBy, attemptOn)
            VALUES (@RequestLogId, @ResponseJson, @AttemptStatus, @FailureError, @AttemptBy, @AttemptOn);
            SELECT LAST_INSERT_ID();
            """, attempt, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<RequestResponseAttempt>> GetAttemptsAsync(long requestLogId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RequestResponseAttempt>(new CommandDefinition(
            "SELECT * FROM RequestResponseAttempts WHERE requestLogId = @requestLogId ORDER BY attemptOn",
            new { requestLogId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
