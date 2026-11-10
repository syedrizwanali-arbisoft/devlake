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
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly IRequestLogRepository _requestLogs = Substitute.For<IRequestLogRepository>();
    private readonly IdempotencyCheck _check;

    public IdempotencyCheckTests()
    {
        _check = new IdempotencyCheck(_requestLogs, Options.Create(new RequestOptions { DuplicateWindowSeconds = 300 }),
            new FakeTimeProvider(Now));
    }

    private static TimeFrame Tf(string value) => TimeFrame.TryParse(value, out var tf) ? tf : throw new ArgumentException(value);

    [Fact]
    public void ComputeHash_IgnoresEmailOrder()
    {
        Assert.Equal(
            _check.ComputeHash("e", ["a@arbisoft.com", "b@arbisoft.com"], Tf("1d")),
            _check.ComputeHash("e", ["b@arbisoft.com", "a@arbisoft.com"], Tf("1d")));
    }

    [Fact]
    public void ComputeHash_DependsOnEventTypeTimeFrameAndEmails()
    {
        var baseline = _check.ComputeHash("e", ["a@arbisoft.com"], Tf("1d"));

        Assert.Matches("^[0-9a-f]{64}$", baseline);
        Assert.NotEqual(baseline, _check.ComputeHash("other", ["a@arbisoft.com"], Tf("1d")));
        Assert.NotEqual(baseline, _check.ComputeHash("e", ["a@arbisoft.com"], Tf("24h")));
        Assert.NotEqual(baseline, _check.ComputeHash("e", ["b@arbisoft.com"], Tf("1d")));
        Assert.NotEqual(baseline, _check.ComputeHash("e", ["a@arbisoft.com", "b@arbisoft.com"], Tf("1d")));
    }

    [Fact]
    public async Task FindDuplicate_SearchesTheWindowAndReturnsTheOriginalCorrelationId()
    {
        _requestLogs.FindRecentDuplicateAsync(1, "hash", Now.AddMinutes(-5), Arg.Any<CancellationToken>())
            .Returns(new RequestLog { CorrelationId = "original" });

        Assert.Equal("original", await _check.FindDuplicateAsync(1, "hash"));
    }

    [Fact]
    public async Task FindDuplicate_ReturnsNullWithoutAMatch()
    {
        Assert.Null(await _check.FindDuplicateAsync(1, "hash"));
    }
}
