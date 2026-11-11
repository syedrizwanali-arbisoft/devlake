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

using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public class StaleRequestSweeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IRequestAttemptLog _attempts = Substitute.For<IRequestAttemptLog>();
    private readonly StaleRequestSweeper _sweeper;

    public StaleRequestSweeperTests()
    {
        _logs.ListStaleAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _sweeper = new StaleRequestSweeper(
            _logs,
            _attempts,
            Options.Create(new RabbitMqOptions { MessageTtlSeconds = 7200 }),
            Options.Create(new ProcessingOptions { StaleGraceSeconds = 60 }),
            new FakeTimeProvider(Now),
            NullLogger<StaleRequestSweeper>.Instance);
    }

    [Fact]
    public async Task OnlyUnfinishedRequests_OlderThanTheQueueLifetimePlusGrace_AreLookedFor()
    {
        await _sweeper.SweepAsync(CancellationToken.None);

        await _logs.Received(1).ListStaleAsync(
            Arg.Is<IReadOnlyCollection<string>>(statuses => statuses.Order().SequenceEqual(new[] { RequestStatus.Processing, RequestStatus.Queued, RequestStatus.Received }.Order())),
            Now.AddSeconds(-(7200 + 60)),
            100,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachStaleRequest_IsMarkedFailed_WithAnExpiredAttemptThatSaysWhy()
    {
        _logs.ListStaleAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new RequestLog { Id = 1, CorrelationId = "a" }, new RequestLog { Id = 2, CorrelationId = "b" }]);

        var swept = await _sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(2, swept);
        foreach (var id in new long[] { 1, 2 })
        {
            await _logs.Received(1).UpdateStatusAsync(id, RequestStatus.Failed, Arg.Any<CancellationToken>());
            await _attempts.Received(1).RecordAsync(id, DeliveryAttemptStatus.Expired, "{}", Arg.Is<string?>(reason => reason != null && reason.Contains("expired")), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task WithNothingStale_NothingIsChanged()
    {
        Assert.Equal(0, await _sweeper.SweepAsync(CancellationToken.None));

        await _logs.DidNotReceiveWithAnyArgs().UpdateStatusAsync(default, default!, default);
    }
}
