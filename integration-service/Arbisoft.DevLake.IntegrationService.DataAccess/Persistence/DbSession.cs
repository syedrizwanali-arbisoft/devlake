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

using System.Data.Common;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

/// <inheritdoc />
public sealed class DbSession : IDbSession
{
    private readonly IDbConnectionFactory _connectionFactory;
    private DbConnection? _connection;

    public DbSession(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public DbTransaction? Transaction { get; private set; }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        _connection ??= await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return _connection;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open on this session.");
        }

        var connection = await GetConnectionAsync(cancellationToken);
        Transaction = await connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (Transaction is null)
        {
            throw new InvalidOperationException("There is no open transaction to commit.");
        }

        await Transaction.CommitAsync(cancellationToken);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    /// <summary>Disposing with an open transaction rolls it back.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null)
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
