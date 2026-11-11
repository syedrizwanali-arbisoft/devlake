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

/// <summary>Opens pooled MySQL connections from a single shared data source.</summary>
public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly MySqlDataSource _dataSource;

    public MySqlConnectionFactory(MySqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await _dataSource.OpenConnectionAsync(cancellationToken);
    }
}
