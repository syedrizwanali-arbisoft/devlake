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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IRequestLogService
{
    Task<RequestLogPage> SearchAsync(RequestLogFilter filter, CancellationToken cancellationToken = default);
    Task<RequestLogDetailsDto?> GetAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class RequestLogService(IRequestLogRepository requestLogs, IClientRepository clients) : IRequestLogService
{
    public const int MaxPageSize = 200;

    public static readonly IReadOnlyList<string> Statuses =
    [
        RequestLogStatus.Received, RequestLogStatus.Queued, RequestLogStatus.Processing, RequestLogStatus.Processed,
        RequestLogStatus.Success, RequestLogStatus.Failed, RequestLogStatus.RevokedNoCallbackUrl,
    ];

    public async Task<RequestLogPage> SearchAsync(RequestLogFilter filter, CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var page = Math.Max(filter.Page, 1);
        var status = Statuses.Contains(filter.Status) ? filter.Status : null;
        DateTime? from = filter.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime? to = filter.To?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var (logs, total) = await requestLogs.SearchAsync(filter.ClientRefId, status, from, to, pageSize, (page - 1) * pageSize, cancellationToken);
        var names = await ClientNamesAsync(cancellationToken);
        return new RequestLogPage(logs.Select(l => ToDto(l, names)).ToList(), total, page, pageSize);
    }

    public async Task<RequestLogDetailsDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var log = await requestLogs.GetByIdAsync(id, cancellationToken);
        if (log is null)
        {
            return null;
        }

        var json = await requestLogs.GetJsonAsync(id, cancellationToken);
        var attempts = (await requestLogs.GetAttemptsAsync(id, cancellationToken))
            .Select(a => new RequestAttemptDto(a.Id, a.AttemptStatus, a.FailureError, a.ResponseJson, a.AttemptBy, a.AttemptOn))
            .ToList();
        return new RequestLogDetailsDto(ToDto(log, await ClientNamesAsync(cancellationToken)), json, attempts);
    }

    private async Task<Dictionary<long, (string ClientId, string Name)>> ClientNamesAsync(CancellationToken cancellationToken) =>
        (await clients.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id, c => (c.ClientId, c.Name));

    private static RequestLogDto ToDto(RequestLog l, Dictionary<long, (string ClientId, string Name)> names)
    {
        var (clientId, name) = names.TryGetValue(l.ClientRefId, out var found) ? found : ("(unknown)", "(unknown)");
        return new RequestLogDto(l.Id, l.CorrelationId, l.ClientRefId, clientId, name, l.Url, l.Status, l.EventType, l.CreatedOn);
    }
}
