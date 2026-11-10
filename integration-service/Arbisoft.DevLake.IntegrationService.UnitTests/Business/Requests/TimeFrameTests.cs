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

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class TimeFrameTests
{
    private static readonly DateTime End = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("24h", 24, 'h')]
    [InlineData("1w", 1, 'w')]
    [InlineData("30d", 30, 'd')]
    [InlineData("11M", 11, 'M')]
    [InlineData("1y", 1, 'y')]
    [InlineData("999d", 999, 'd')]
    public void TryParse_AcceptsTheContractFormats(string value, int amount, char unit)
    {
        Assert.True(TimeFrame.TryParse(value, out var timeFrame));
        Assert.Equal(amount, timeFrame.Amount);
        Assert.Equal(unit, timeFrame.Unit);
        Assert.Equal(value, timeFrame.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24")]
    [InlineData("h")]
    [InlineData("24H")]
    [InlineData("1m")]
    [InlineData("1D")]
    [InlineData("0d")]
    [InlineData("01d")]
    [InlineData("1000d")]
    [InlineData("-1d")]
    [InlineData(" 1d")]
    [InlineData("1d ")]
    [InlineData("1.5d")]
    [InlineData("1dd")]
    public void TryParse_RejectsAnythingElse(string? value)
    {
        Assert.False(TimeFrame.TryParse(value, out _));
    }

    [Theory]
    [InlineData("24h", "2026-11-09T12:00:00")]
    [InlineData("2d", "2026-11-08T12:00:00")]
    [InlineData("1w", "2026-11-03T12:00:00")]
    [InlineData("11M", "2025-12-10T12:00:00")]
    [InlineData("1y", "2025-11-10T12:00:00")]
    public void StartBefore_CountsBackFromTheEnd(string value, string expected)
    {
        Assert.True(TimeFrame.TryParse(value, out var timeFrame));

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), timeFrame.StartBefore(End));
    }
}
