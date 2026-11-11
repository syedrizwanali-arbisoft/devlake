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

using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class IdempotencyCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IRequestLogRepository _requestLogs = Substitute.For<IRequestLogRepository>();
    private readonly FakeTimeProvider _time = new(Now);

    private IdempotencyCheck Create(int windowMinutes = 15)
    {
        var options = Substitute.For<IOptionsSnapshot<RequestsOptions>>();
        options.Value.Returns(new RequestsOptions { DuplicateWindowMinutes = windowMinutes });
        return new IdempotencyCheck(_requestLogs, options, _time);
    }

    private static TimeFrame Frame(string text)
    {
        return TimeFrame.TryParse(text, out var timeFrame, out _) ? timeFrame : throw new ArgumentException("Not a time frame.", nameof(text));
    }

    private void Existing(RequestLog? log) => _requestLogs.GetByIdempotencyKeyAsync("key", Arg.Any<CancellationToken>()).Returns(log);

    [Fact]
    public void ComputeKey_IsStable_AndIgnoresTheOrderOfEmails()
    {
        var check = Create();

        var first = check.ComputeKey(1, "engineer-stats-batch", Frame("24h"), ["a@arbisoft.com", "b@arbisoft.com"]);
        var reordered = check.ComputeKey(1, "engineer-stats-batch", Frame("24h"), ["b@arbisoft.com", "a@arbisoft.com"]);

        Assert.Equal(first, reordered);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void ComputeKey_DiffersByClientEventTypeTimeFrameAndEmails()
    {
        var check = Create();
        var baseline = check.ComputeKey(1, "engineer-stats", Frame("24h"), ["a@arbisoft.com"]);

        Assert.NotEqual(baseline, check.ComputeKey(2, "engineer-stats", Frame("24h"), ["a@arbisoft.com"]));
        Assert.NotEqual(baseline, check.ComputeKey(1, "engineer-stats-batch", Frame("24h"), ["a@arbisoft.com"]));
        Assert.NotEqual(baseline, check.ComputeKey(1, "engineer-stats", Frame("1w"), ["a@arbisoft.com"]));
        Assert.NotEqual(baseline, check.ComputeKey(1, "engineer-stats", Frame("24h"), ["a@arbisoft.com", "b@arbisoft.com"]));
        Assert.NotEqual(baseline, check.ComputeKey(1, "engineer-stats", Frame("24h"), ["b@arbisoft.com"]));
    }

    [Fact]
    public void ComputeKey_CannotBeFooledByFieldBoundaries()
    {
        var check = Create();

        Assert.NotEqual(
            check.ComputeKey(1, "engineer-stats", Frame("24h"), ["a@arbisoft.com,b@arbisoft.com"]),
            check.ComputeKey(1, "engineer-stats", Frame("24h"), ["a@arbisoft.com", "b@arbisoft.com"]));
    }

    [Fact]
    public async Task FindDuplicate_NothingStored_ReturnsNull()
    {
        Existing(null);

        Assert.Null(await Create().FindDuplicateAsync("key", CancellationToken.None));
    }

    [Theory]
    [InlineData(RequestStatus.Queued)]
    [InlineData(RequestStatus.Processing)]
    [InlineData(RequestStatus.Processed)]
    [InlineData(RequestStatus.Success)]
    public async Task FindDuplicate_InsideTheWindow_ReturnsTheEarlierRequest(string status)
    {
        var earlier = new RequestLog { Id = 5, Status = status, CreatedOn = Now.AddMinutes(-14) };
        Existing(earlier);

        Assert.Same(earlier, await Create().FindDuplicateAsync("key", CancellationToken.None));
        await _requestLogs.DidNotReceiveWithAnyArgs().ReleaseIdempotencyKeyAsync(default, default);
    }

    [Fact]
    public async Task FindDuplicate_OutsideTheWindow_ReleasesTheKeyOfThatRequest_AndReturnsNull()
    {
        Existing(new RequestLog { Id = 5, Status = RequestStatus.Success, CreatedOn = Now.AddMinutes(-16) });

        Assert.Null(await Create().FindDuplicateAsync("key", CancellationToken.None));

        await _requestLogs.Received(1).ReleaseIdempotencyKeyAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindDuplicate_AFailedRequest_DoesNotBlockARepeat()
    {
        Existing(new RequestLog { Id = 7, Status = RequestStatus.Failed, CreatedOn = Now.AddMinutes(-1) });

        Assert.Null(await Create().FindDuplicateAsync("key", CancellationToken.None));

        await _requestLogs.Received(1).ReleaseIdempotencyKeyAsync(7, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(1, 0.5, true)]
    [InlineData(60, 59, true)]
    [InlineData(60, 61, false)]
    public async Task FindDuplicate_UsesTheConfiguredWindow(int windowMinutes, double ageMinutes, bool isDuplicate)
    {
        Existing(new RequestLog { Id = 5, Status = RequestStatus.Queued, CreatedOn = Now.AddMinutes(-ageMinutes) });

        var found = await Create(windowMinutes).FindDuplicateAsync("key", CancellationToken.None);

        Assert.Equal(isDuplicate, found is not null);
    }
}
