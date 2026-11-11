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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public interface IEngineerStatsCollector
{
    /// <summary>
    /// Gets Jira, GitHub, AI usage and AI cost stats from DevLake for an engineer's Arbisoft email and for every other email they commit with,
    /// all at once, and merges them into one result. If any call fails the whole collection fails, so a result is never delivered with a gap.
    /// It makes HTTP calls only and never touches the database, so many engineers can be collected in parallel without sharing a connection (DAT-16).
    /// </summary>
    /// <param name="arbisoftEmail">The engineer's Arbisoft email.</param>
    /// <param name="mappedEmails">The other emails they commit with, as stored. They are canonicalized and de-duplicated here.</param>
    /// <param name="query">The period to ask for.</param>
    /// <param name="correlationId">The request's correlation ID, sent with every call.</param>
    /// <param name="cancellationToken">Cancels the collection.</param>
    /// <exception cref="DataAccess.ExternalApis.ExternalApiException">DevLake refused, rejected or could not be reached.</exception>
    Task<EmployeeStatsDto> CollectAsync(string arbisoftEmail, IReadOnlyList<string> mappedEmails, StatsQuery query, string correlationId, CancellationToken cancellationToken);
}
