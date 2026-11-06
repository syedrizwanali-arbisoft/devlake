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
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

public sealed class MySqlConnectionFactory(string? connectionString) : IDbConnectionFactory
{
    /// <summary>
    /// The configured string, for DbUp. DbUp writes its journal timestamp as a local DateTime, which
    /// MySqlConnector rejects when DateTimeKind is Utc.
    /// </summary>
    public string MigrationConnectionString => !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new InvalidOperationException(
            $"Connection string '{DatabaseOptions.ConnectionStringName}' is not set. " +
            $"Set it through the ConnectionStrings__{DatabaseOptions.ConnectionStringName} environment variable.");

    // All timestamps are stored as UTC, and reading them back as UTC keeps expiry checks correct.
    public string ConnectionString =>
        new MySqlConnectionStringBuilder(MigrationConnectionString) { DateTimeKind = MySqlDateTimeKind.Utc }.ConnectionString;

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
