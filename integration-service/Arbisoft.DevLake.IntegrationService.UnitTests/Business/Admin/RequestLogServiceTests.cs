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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Admin;

public class RequestLogServiceTests
{
    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly RequestLogService _service;

    public RequestLogServiceTests()
    {
        _clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ClientBuilder().Build()]);
        _logs.SearchAsync(default, default, default, default, default, default).ReturnsForAnyArgs(
            ((IReadOnlyList<RequestLog>)[new RequestLog { Id = 4, ClientRefId = 1, CorrelationId = "c", Status = "Queued" }], 120));
        _service = new RequestLogService(_logs, _clients);
    }

    [Fact]
    public async Task Search_TurnsDaysIntoAnInclusiveUtcRangeAndPages()
    {
        var page = await _service.SearchAsync(new RequestLogFilter(1, "Failed", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), Page: 3, PageSize: 50));

        await _logs.Received(1).SearchAsync(1, "Failed",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), 50, 100, Arg.Any<CancellationToken>());
        Assert.Equal(3, page.PageCount);
        Assert.Equal("sophia", page.Logs.Single().ClientId);
    }

    [Fact]
    public async Task Search_IgnoresAnUnknownStatusAndClampsPaging()
    {
        await _service.SearchAsync(new RequestLogFilter(null, "DROP TABLE", null, null, Page: -4, PageSize: 10_000));

        await _logs.Received(1).SearchAsync(null, null, null, null, RequestLogService.MaxPageSize, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsTheBodyAndAttempts()
    {
        _logs.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new RequestLog { Id = 4, ClientRefId = 1, CorrelationId = "c", Status = "Success" });
        _logs.GetJsonAsync(4, Arg.Any<CancellationToken>()).Returns("{\"a\":1}");
        _logs.GetAttemptsAsync(4, Arg.Any<CancellationToken>()).Returns([
            new RequestResponseAttempt { Id = 1, AttemptStatus = "Failed", FailureError = "502" },
            new RequestResponseAttempt { Id = 2, AttemptStatus = "Success" }]);

        var details = await _service.GetAsync(4);

        Assert.Equal("{\"a\":1}", details!.Json);
        Assert.Equal(["Failed", "Success"], details.Attempts.Select(a => a.AttemptStatus));
        Assert.Equal("Sophia", details.Log.ClientName);
    }

    [Fact]
    public async Task Get_ReturnsNullForAnUnknownLog()
    {
        _logs.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((RequestLog?)null);

        Assert.Null(await _service.GetAsync(99));
    }
}
