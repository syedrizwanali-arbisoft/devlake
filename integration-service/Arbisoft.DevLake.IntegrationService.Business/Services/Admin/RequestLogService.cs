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
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IRequestLogService
{
    /// <summary>The request statuses a list can be filtered by.</summary>
    IReadOnlyList<string> Statuses { get; }

    Task<PageDto<RequestLogDto>> ListAsync(RequestLogQuery query, int page, CancellationToken cancellationToken);

    Task<RequestLogDetailsDto?> GetAsync(long id, CancellationToken cancellationToken);
}

public sealed class RequestLogService : IRequestLogService
{
    public const int PageSize = 50;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly IRequestLogRepository _logs;
    private readonly IClientRepository _clients;

    public RequestLogService(IRequestLogRepository logs, IClientRepository clients)
    {
        _logs = logs;
        _clients = clients;
    }

    public IReadOnlyList<string> Statuses { get; } =
    [
        RequestStatus.Received, RequestStatus.Queued, RequestStatus.Processing, RequestStatus.Processed,
        RequestStatus.Success, RequestStatus.Failed, RequestStatus.RevokedNoCallbackUrl
    ];

    public async Task<PageDto<RequestLogDto>> ListAsync(RequestLogQuery query, int page, CancellationToken cancellationToken)
    {
        var filter = new RequestLogFilter(
            query.ClientRefId,
            string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
            query.From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            query.To is { } to ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null);
        var result = await _logs.ListAsync(filter, page, PageSize, cancellationToken);
        var clients = new Dictionary<long, Client?>();
        var items = new List<RequestLogDto>(result.Items.Count);
        foreach (var log in result.Items)
        {
            items.Add(ToDto(log, await ClientAsync(clients, log.ClientRefId, cancellationToken)));
        }

        return new PageDto<RequestLogDto>(items, result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<RequestLogDetailsDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var log = await _logs.GetByIdAsync(id, cancellationToken);
        if (log is null)
        {
            return null;
        }

        var client = await _clients.GetByIdAsync(log.ClientRefId, cancellationToken);
        var json = await _logs.GetJsonAsync(id, cancellationToken);
        var attempts = (await _logs.GetAttemptsAsync(id, cancellationToken))
            .Select(a => new DeliveryAttemptDto(a.AttemptOn, a.AttemptStatus, a.AttemptBy, a.FailureError, Indent(a.ResponseJson)!))
            .ToList();
        return new RequestLogDetailsDto(
            log.Id, client?.ClientId ?? string.Empty, client?.Name ?? string.Empty, log.CorrelationId, log.EventType, log.Status, log.Url, log.CreatedOn, Indent(json), attempts);
    }

    private async Task<Client?> ClientAsync(Dictionary<long, Client?> cache, long id, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(id, out var client))
        {
            client = await _clients.GetByIdAsync(id, cancellationToken);
            cache[id] = client;
        }

        return client;
    }

    /// <summary>Pretty-prints JSON for reading. Text that is not JSON, such as a plain error body from the client, is shown as it is.</summary>
    private static string? Indent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        try
        {
            return JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement, Indented);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static RequestLogDto ToDto(RequestLog log, Client? client) =>
        new(log.Id, client?.ClientId ?? string.Empty, client?.Name ?? string.Empty, log.CorrelationId, log.EventType, log.Status, log.CreatedOn);
}
