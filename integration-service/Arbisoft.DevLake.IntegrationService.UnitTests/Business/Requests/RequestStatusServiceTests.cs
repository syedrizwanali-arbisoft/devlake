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

using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestStatusServiceTests
{
    private static readonly Guid Id = Guid.Parse("0B8F3C52-1111-4222-8333-444455556666");

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 14, 2, 55, 987, TimeSpan.Zero));
    private readonly RequestStatusService _service;

    public RequestStatusServiceTests()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new Client { Id = 7, ClientId = "sophia", IsActive = true });
        _service = new RequestStatusService(_clients, _logs, _time);
    }

    private void Stored(string status, long clientRefId = 7)
    {
        _logs.GetByCorrelationIdAsync("0b8f3c52-1111-4222-8333-444455556666", Arg.Any<CancellationToken>())
            .Returns(new RequestLog { ClientRefId = clientRefId, CorrelationId = "0b8f3c52-1111-4222-8333-444455556666", Status = status });
    }

    [Theory]
    [InlineData(RequestStatus.Received, "Queued")]
    [InlineData(RequestStatus.Queued, "Queued")]
    [InlineData(RequestStatus.Processing, "Processing")]
    [InlineData(RequestStatus.Processed, "Processing")]
    [InlineData(RequestStatus.Success, "Success")]
    [InlineData(RequestStatus.Failed, "Failure")]
    [InlineData(RequestStatus.RevokedNoCallbackUrl, "Revoked")]
    public async Task EveryRequestLogStatus_MapsToTheDocumentedPollStatus(string stored, string expected)
    {
        Stored(stored);

        var response = await _service.GetStatusAsync("sophia", Id, CancellationToken.None);

        Assert.Equal(expected, response!.Status);
    }

    [Fact]
    public void TheMapping_CoversEveryStatusARequestLogCanHold()
    {
        var statuses = typeof(RequestStatus).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!).ToList();

        Assert.Equal(7, statuses.Count);
        Assert.All(statuses, status => Assert.Contains(PollStatus.From(status), new[] { "Queued", "Processing", "Success", "Failure", "Revoked" }));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("Revoked")]
    [InlineData("")]
    [InlineData("Cancelled")]
    public void AStatusNotInTheTable_IsADefect_NotAGuess(string unknown)
    {
        Assert.Throws<InvalidOperationException>(() => PollStatus.From(unknown));
    }

    [Fact]
    public async Task TheResponse_CarriesTheCorrelationId_AndTheCheckTimeInUtcToTheSecond()
    {
        Stored(RequestStatus.Processing);

        var response = await _service.GetStatusAsync("sophia", Id, CancellationToken.None);

        Assert.Equal("0b8f3c52-1111-4222-8333-444455556666", response!.CorrelationId);
        Assert.Equal("2026-11-05T14:02:55Z", response.CheckTime);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"), response.CheckTime);
    }

    [Fact]
    public async Task TheCheckTime_IsTheTimeOfTheCall_NotOfTheRequest()
    {
        Stored(RequestStatus.Queued);
        var first = await _service.GetStatusAsync("sophia", Id, CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(5));
        var second = await _service.GetStatusAsync("sophia", Id, CancellationToken.None);

        Assert.Equal("2026-11-05T14:02:55Z", first!.CheckTime);
        Assert.Equal("2026-11-05T14:07:55Z", second!.CheckTime);
    }

    [Fact]
    public async Task ARequestOfAnotherClient_LooksExactlyLikeAnUnknownOne()
    {
        Stored(RequestStatus.Success, clientRefId: 99);

        Assert.Null(await _service.GetStatusAsync("sophia", Id, CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownCorrelationId_ReturnsNull()
    {
        Assert.Null(await _service.GetStatusAsync("sophia", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownClient_ReturnsNull_WithoutLookingAtRequests()
    {
        Assert.Null(await _service.GetStatusAsync("nobody", Id, CancellationToken.None));

        await _logs.DidNotReceiveWithAnyArgs().GetByCorrelationIdAsync(default!, default);
    }

    [Fact]
    public async Task TheLookup_UsesTheLowerCaseFormTheIdWasStoredIn()
    {
        await _service.GetStatusAsync("sophia", Id, CancellationToken.None);

        await _logs.Received(1).GetByCorrelationIdAsync("0b8f3c52-1111-4222-8333-444455556666", Arg.Any<CancellationToken>());
    }
}
