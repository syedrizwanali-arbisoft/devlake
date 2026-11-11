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

using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class EngineerStatsCollector : IEngineerStatsCollector
{
    private static readonly DevLakeStatsSource[] Sources = [DevLakeStatsSource.Jira, DevLakeStatsSource.Github, DevLakeStatsSource.AiUsage, DevLakeStatsSource.AiCost];

    private readonly IDevLakeClient _devLake;

    public EngineerStatsCollector(IDevLakeClient devLake)
    {
        _devLake = devLake;
    }

    public async Task<EmployeeStatsDto> CollectAsync(
        string arbisoftEmail, IReadOnlyList<string> mappedEmails, StatsQuery query, string correlationId, CancellationToken cancellationToken)
    {
        var mapped = mappedEmails
            .Select(EmailNormalizer.Canonical)
            .Where(email => !string.Equals(email, arbisoftEmail, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var emails = new[] { arbisoftEmail }.Concat(mapped).ToList();

        var calls = emails.SelectMany(email => Sources.Select(source => (email, source, task: _devLake.GetStatsAsync(source, email, query, correlationId, cancellationToken)))).ToList();
        await UpstreamFailures.AwaitAllAsync(calls.Select(call => call.task).ToList());

        IReadOnlyDictionary<string, JsonElement> Of(DevLakeStatsSource source) =>
            calls.Where(call => call.source == source).ToDictionary(call => call.email, call => call.task.Result, StringComparer.OrdinalIgnoreCase);

        return new EmployeeStatsDto(arbisoftEmail, mapped, Of(DevLakeStatsSource.Jira), Of(DevLakeStatsSource.Github), Of(DevLakeStatsSource.AiUsage), Of(DevLakeStatsSource.AiCost));
    }
}
