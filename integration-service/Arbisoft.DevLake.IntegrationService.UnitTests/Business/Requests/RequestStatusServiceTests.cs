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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestStatusServiceTests
{
    private const string Id = "3eef2f41-18a6-45e6-b620-e11aa9c69812";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 14, 2, 55, 123, TimeSpan.Zero);

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IRequestLogRepository _requestLogs = Substitute.For<IRequestLogRepository>();
    private readonly RequestStatusService _service;

    public RequestStatusServiceTests()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        _requestLogs.GetByCorrelationIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RequestLog?)null);
        _service = new RequestStatusService(_clients, _requestLogs, new FakeTimeProvider(Now));
    }

    private void Store(string status, long clientRefId = 1) =>
        _requestLogs.GetByCorrelationIdAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new RequestLogBuilder().WithCorrelationId(Id).ForClient(clientRefId).WithStatus(status).Build());

    [Theory]
    [InlineData(RequestLogStatus.Received, "Queued")]
    [InlineData(RequestLogStatus.Queued, "Queued")]
    [InlineData(RequestLogStatus.Processing, "Processing")]
    [InlineData(RequestLogStatus.Processed, "Processing")]
    [InlineData(RequestLogStatus.Success, "Success")]
    [InlineData(RequestLogStatus.Failed, "Failure")]
    [InlineData(RequestLogStatus.RevokedNoCallbackUrl, "Revoked")]
    public async Task GetStatus_MapsTheLogStatusToTheContractStatus(string logStatus, string pollStatus)
    {
        Store(logStatus);

        var result = await _service.GetStatusAsync("sophia", Id);

        Assert.Equal(RequestStatusOutcome.Found, result.Outcome);
        Assert.Equal(new RequestStatusResponse(Id, pollStatus, "2026-11-05T14:02:55Z"), result.Response);
    }

    [Fact]
    public void ToPollStatus_FailsLoudlyForAnUnknownStatus()
    {
        Assert.Throws<InvalidOperationException>(() => RequestStatusService.ToPollStatus("Something new"));
    }

    [Theory]
    [InlineData("3EEF2F41-18A6-45E6-B620-E11AA9C69812")]
    [InlineData("{3eef2f41-18a6-45e6-b620-e11aa9c69812}")]
    public async Task GetStatus_NormalizesTheId(string id)
    {
        Store(RequestLogStatus.Queued);

        var result = await _service.GetStatusAsync("sophia", id);

        Assert.Equal(Id, result.Response!.CorrelationId);
    }

    [Fact]
    public async Task GetStatus_HidesAnotherClientsRequest()
    {
        Store(RequestLogStatus.Success, clientRefId: 99);

        var result = await _service.GetStatusAsync("sophia", Id);

        Assert.Equal(new RequestStatusResult(RequestStatusOutcome.NotFound, null), result);
    }

    [Fact]
    public async Task GetStatus_ReturnsNotFoundForAnUnknownId()
    {
        Assert.Equal(RequestStatusOutcome.NotFound, (await _service.GetStatusAsync("sophia", Id)).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("3eef2f41-18a6-45e6-b620")]
    public async Task GetStatus_RejectsAnInvalidId(string? id)
    {
        Assert.Equal(RequestStatusOutcome.InvalidId, (await _service.GetStatusAsync("sophia", id)).Outcome);
        await _requestLogs.DidNotReceiveWithAnyArgs().GetByCorrelationIdAsync(default!, default);
    }
}
