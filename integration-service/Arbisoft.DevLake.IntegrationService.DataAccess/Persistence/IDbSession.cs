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

/// <summary>
/// The connection and optional transaction shared by the repositories of one unit of work.
/// Services begin and commit transactions; repositories never do (DAT-15). A session is not thread-safe:
/// use one per request or per batch item, never across parallel tasks.
/// </summary>
public interface IDbSession : IAsyncDisposable
{
    /// <summary>The open transaction, or null when none has been started.</summary>
    DbTransaction? Transaction { get; }

    Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);
}
