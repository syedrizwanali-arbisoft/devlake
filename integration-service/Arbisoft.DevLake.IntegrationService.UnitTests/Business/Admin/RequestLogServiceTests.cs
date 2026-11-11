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
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class RequestLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly RequestLogService _service;

    public RequestLogServiceTests()
    {
        _clients.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Client { Id = 1, ClientId = "sophia", Name = "Sophia" });
        _service = new RequestLogService(_logs, _clients);
    }

    private static RequestLog Log(long id, string status = RequestStatus.Success) =>
        new() { Id = id, ClientRefId = 1, CorrelationId = "corr-" + id, EventType = "engineer-stats", Status = status, Url = "/api/v1/engineers/x/stats", CreatedOn = Now };

    [Fact]
    public async Task List_TurnsTheDayFiltersIntoAUtcRange_WithTheLastDayIncluded()
    {
        _logs.ListAsync(Arg.Any<RequestLogFilter>(), 1, RequestLogService.PageSize, Arg.Any<CancellationToken>()).Returns(new PagedResult<RequestLog>([], 0, 1, RequestLogService.PageSize));

        await _service.ListAsync(new RequestLogQuery(1, RequestStatus.Failed, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)), 1, CancellationToken.None);

        await _logs.Received(1).ListAsync(
            Arg.Is<RequestLogFilter>(f => f.ClientRefId == 1 && f.Status == "Failed" && f.From == new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero) && f.To == new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero)),
            1, RequestLogService.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_WithNoFilters_AsksForEverything()
    {
        _logs.ListAsync(Arg.Any<RequestLogFilter>(), 1, RequestLogService.PageSize, Arg.Any<CancellationToken>()).Returns(new PagedResult<RequestLog>([], 0, 1, RequestLogService.PageSize));

        await _service.ListAsync(new RequestLogQuery(Status: "  "), 1, CancellationToken.None);

        await _logs.Received(1).ListAsync(Arg.Is<RequestLogFilter>(f => f.ClientRefId == null && f.Status == null && f.From == null && f.To == null), 1, RequestLogService.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_NamesTheClient_AndLooksEachOneUpOnce()
    {
        _logs.ListAsync(Arg.Any<RequestLogFilter>(), 1, RequestLogService.PageSize, Arg.Any<CancellationToken>()).Returns(new PagedResult<RequestLog>([Log(1), Log(2)], 2, 1, RequestLogService.PageSize));

        var page = await _service.ListAsync(new RequestLogQuery(), 1, CancellationToken.None);

        Assert.All(page.Items, item => Assert.Equal("sophia", item.ClientId));
        await _clients.Received(1).GetByIdAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ShowsTheBodyIndented_AndEveryAttemptInOrder()
    {
        _logs.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Log(1));
        _logs.GetJsonAsync(1, Arg.Any<CancellationToken>()).Returns("{\"emails\":[\"a@arbisoft.com\"]}");
        _logs.GetAttemptsAsync(1, Arg.Any<CancellationToken>()).Returns(
        [
            new RequestResponseAttempt { AttemptOn = Now, AttemptStatus = "Failed", AttemptBy = "job", FailureError = "HTTP 503", ResponseJson = "{\"error\":\"down\"}" },
            new RequestResponseAttempt { AttemptOn = Now.AddMinutes(1), AttemptStatus = "Success", AttemptBy = "job", ResponseJson = "plain text, not json" }
        ]);

        var details = await _service.GetAsync(1, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Contains("\n", details.Json, StringComparison.Ordinal);
        Assert.Contains("a@arbisoft.com", details.Json, StringComparison.Ordinal);
        Assert.Equal(["Failed", "Success"], details.Attempts.Select(a => a.AttemptStatus));
        Assert.Equal("HTTP 503", details.Attempts[0].FailureError);
        Assert.Contains("\"error\"", details.Attempts[0].ResponseJson, StringComparison.Ordinal);
        Assert.Equal("plain text, not json", details.Attempts[1].ResponseJson);
        Assert.Equal("Sophia", details.ClientName);
    }

    [Fact]
    public async Task Get_ARequestWithNoBodyOrAttempts_StillShows()
    {
        _logs.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Log(2, RequestStatus.Queued));
        _logs.GetJsonAsync(2, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        var details = await _service.GetAsync(2, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Null(details.Json);
        Assert.Empty(details.Attempts);
    }

    [Fact]
    public async Task Get_AnUnknownRequest_IsNull()
    {
        Assert.Null(await _service.GetAsync(99, CancellationToken.None));
    }

    [Fact]
    public void Statuses_AreEveryStatusTheServiceUses()
    {
        Assert.Contains(RequestStatus.RevokedNoCallbackUrl, _service.Statuses);
        Assert.Equal(7, _service.Statuses.Count);
    }
}
