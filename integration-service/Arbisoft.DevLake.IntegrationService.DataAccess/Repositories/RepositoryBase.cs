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

using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Dapper;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

/// <summary>
/// Shared plumbing for the Dapper repositories: every command runs on the unit of work's connection and transaction,
/// carries the configured command timeout, and passes the cancellation token down. SQL is always parameterized.
/// </summary>
public abstract class RepositoryBase
{
    public const int MaxPageSize = 100;

    private readonly IDbSession _session;
    private readonly int _commandTimeoutSeconds;

    protected RepositoryBase(IDbSession session, IOptions<DatabaseOptions> options)
    {
        _session = session;
        _commandTimeoutSeconds = options.Value.CommandTimeoutSeconds;
    }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await _session.GetConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<T>(Command(sql, parameters, cancellationToken));
        return rows.AsList();
    }

    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await _session.GetConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(Command(sql, parameters, cancellationToken));
    }

    /// <summary>Runs a statement and returns the number of rows it changed.</summary>
    protected async Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await _session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, parameters, cancellationToken));
    }

    /// <summary>Runs an INSERT and returns the new row's auto-increment id.</summary>
    protected async Task<long> InsertAsync(string insertSql, object parameters, CancellationToken cancellationToken)
    {
        var connection = await _session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(Command(insertSql + "; SELECT LAST_INSERT_ID();", parameters, cancellationToken));
    }

    /// <summary>
    /// Runs a count and a page query. <paramref name="fromAndWhere"/>, <paramref name="columns"/> and <paramref name="orderBy"/>
    /// must be constants written in the repository; user input only ever travels in <paramref name="parameters"/>.
    /// </summary>
    protected async Task<PagedResult<T>> QueryPageAsync<T>(
        string columns, string fromAndWhere, string orderBy, object? parameters, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var number = Math.Max(page, 1);
        var paging = new DynamicParameters(parameters);
        paging.Add("pageOffset", (number - 1) * size);
        paging.Add("pageLimit", size);

        var total = await QuerySingleOrDefaultAsync<int>($"SELECT COUNT(*) {fromAndWhere}", paging, cancellationToken);
        var items = await QueryAsync<T>($"SELECT {columns} {fromAndWhere} ORDER BY {orderBy} LIMIT @pageLimit OFFSET @pageOffset", paging, cancellationToken);
        return new PagedResult<T>(items, total, number, size);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, _session.Transaction, _commandTimeoutSeconds, cancellationToken: cancellationToken);
    }
}
