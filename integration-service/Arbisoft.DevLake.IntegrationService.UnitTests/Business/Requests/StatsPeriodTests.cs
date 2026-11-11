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

using System.ComponentModel.DataAnnotations;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class StatsPeriodTests
{
    private static StatsPeriod? Parse(string? timeFrame, string? from, string? to, out string? error)
    {
        return StatsPeriod.TryParse(timeFrame, from, to, out var period, out error) ? period : null;
    }

    [Fact]
    public void ATimeFrame_IsAccepted_AsItWasWritten()
    {
        var period = Parse("24h", null, null, out var error);

        Assert.Null(error);
        Assert.Equal("24h", period!.TimeFrame!.Value.Text);
        Assert.Null(period.From);
        Assert.Null(period.To);
        Assert.Equal("tf:24h", period.Canonical);
    }

    [Theory]
    [InlineData("2026-11-01T00:00:00Z", "2026-11-08T00:00:00Z", "2026-11-01T00:00:00Z", "2026-11-08T00:00:00Z")]
    [InlineData("2026-11-01T05:00:00+05:00", "2026-11-08T05:30:00+05:30", "2026-11-01T00:00:00Z", "2026-11-08T00:00:00Z")]
    [InlineData("2026-10-31T19:00:00-05:00", "2026-11-07T19:00:00-05:00", "2026-11-01T00:00:00Z", "2026-11-08T00:00:00Z")]
    [InlineData("2026-11-01T00:00:00.9876543Z", "2026-11-08T00:00:00.5Z", "2026-11-01T00:00:00Z", "2026-11-08T00:00:00Z")]
    public void ARange_IsConvertedToUtc_AtSecondPrecision(string from, string to, string expectedFrom, string expectedTo)
    {
        var period = Parse(null, from, to, out var error);

        Assert.Null(error);
        Assert.Null(period!.TimeFrame);
        Assert.Equal(StatsPeriod.FormatInstant(DateTimeOffset.Parse(expectedFrom, System.Globalization.CultureInfo.InvariantCulture)), StatsPeriod.FormatInstant(period.From!.Value));
        Assert.Equal(expectedFrom, StatsPeriod.FormatInstant(period.From.Value));
        Assert.Equal(expectedTo, StatsPeriod.FormatInstant(period.To!.Value));
        Assert.Equal($"range:{expectedFrom}/{expectedTo}", period.Canonical);
        Assert.Equal(TimeSpan.Zero, period.From.Value.Offset);
    }

    [Fact]
    public void ARangeOfExactlyOneYear_IsAccepted_AndOneSecondMoreIsNot()
    {
        Assert.NotNull(Parse(null, "2026-01-01T00:00:00Z", "2027-01-01T00:00:00Z", out _));

        Parse(null, "2026-01-01T00:00:00Z", "2027-01-01T00:00:01Z", out var error);
        Assert.Equal("The range cannot be longer than one year.", error);
    }

    [Theory]
    [InlineData("2026-11-08T00:00:00Z", "2026-11-01T00:00:00Z")]
    [InlineData("2026-11-01T00:00:00Z", "2026-11-01T00:00:00Z")]
    [InlineData("2026-11-01T00:00:00.1Z", "2026-11-01T00:00:00.9Z")]
    public void AnEmptyOrBackwardsRange_IsRejected(string from, string to)
    {
        Assert.Null(Parse(null, from, to, out var error));

        Assert.Equal("from must be earlier than to.", error);
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-08")]
    [InlineData("2026-11-01T00:00:00", "2026-11-08T00:00:00")]
    [InlineData("2026-11-01 00:00:00Z", "2026-11-08T00:00:00Z")]
    [InlineData("01/11/2026", "08/11/2026")]
    [InlineData("1793491200", "1794096000")]
    [InlineData("yesterday", "today")]
    [InlineData("2026-13-01T00:00:00Z", "2026-13-08T00:00:00Z")]
    [InlineData("2026-11-01T25:00:00Z", "2026-11-08T00:00:00Z")]
    [InlineData("2026-11-01T00:00:00Z", "2026-11-08T00:00:00 05:00")]
    public void ATimestampWithoutATimeOrAnOffset_OrNotISO8601_IsRejected(string from, string to)
    {
        Assert.Null(Parse(null, from, to, out var error));

        Assert.Contains("ISO 8601", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, "Provide tf, or both from and to.")]
    [InlineData("", "", "Provide tf, or both from and to.")]
    [InlineData("24h", "2026-11-01T00:00:00Z", "Provide either tf, or both from and to, not both.")]
    [InlineData("24h", null, null)]
    [InlineData(null, "2026-11-01T00:00:00Z", "from and to must be given together.")]
    [InlineData(null, null, "from and to must be given together.")]
    public void TheModes_CannotBeMixed_OrLeftOut_OrHalfGiven(string? timeFrame, string? from, string? expectedError)
    {
        var to = expectedError == "from and to must be given together." && from is null ? "2026-11-08T00:00:00Z" : null;

        var period = Parse(timeFrame, from, to, out var error);

        Assert.Equal(expectedError, error);
        Assert.Equal(expectedError is null, period is not null);
    }

    [Fact]
    public void AnInvalidTimeFrame_KeepsItsOwnMessage()
    {
        Assert.Null(Parse("24H", null, null, out var error));

        Assert.Contains("case-sensitive", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatInstant_IsUtcWithATrailingZ()
    {
        Assert.Equal("2026-11-05T14:02:55Z", StatsPeriod.FormatInstant(new DateTimeOffset(2026, 11, 5, 19, 2, 55, TimeSpan.FromHours(5))));
    }

    [Theory]
    [InlineData("24h", null, null, true, "tf")]
    [InlineData(null, null, null, false, "tf")]
    [InlineData("24h", "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", false, "tf")]
    [InlineData(null, "2026-11-01T00:00:00Z", null, false, "to")]
    [InlineData(null, null, "2026-11-01T00:00:00Z", false, "from")]
    [InlineData(null, "nonsense", "2026-11-02T00:00:00Z", false, "from")]
    [InlineData(null, "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", true, "from")]
    public void TheQueryModel_ReportsTheParameterThatNeedsFixing(string? timeFrame, string? from, string? to, bool valid, string member)
    {
        var query = new StatsPeriodQuery { TimeFrame = timeFrame, From = from, To = to };

        var results = query.Validate(new ValidationContext(query)).ToList();

        Assert.Equal(valid, results.Count == 0);
        if (!valid)
        {
            Assert.Equal(member, Assert.Single(Assert.Single(results).MemberNames));
        }
    }
}
