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
    [Theory]
    [InlineData("24h", 24, 'h', 24)]
    [InlineData("1h", 1, 'h', 1)]
    [InlineData("30d", 30, 'd', 720)]
    [InlineData("1w", 1, 'w', 168)]
    [InlineData("52w", 52, 'w', 8736)]
    [InlineData("11M", 11, 'M', 7920)]
    [InlineData("12M", 12, 'M', 8640)]
    [InlineData("1y", 1, 'y', 8760)]
    [InlineData("365d", 365, 'd', 8760)]
    [InlineData("8760h", 8760, 'h', 8760)]
    public void TryParse_AcceptsTheDocumentedFormats(string text, int value, char unit, double hours)
    {
        Assert.True(TimeFrame.TryParse(text, out var timeFrame, out var error));

        Assert.Equal(value, timeFrame.Value);
        Assert.Equal(unit, timeFrame.Unit);
        Assert.Equal(text, timeFrame.Text);
        Assert.Equal(TimeSpan.FromHours(hours), timeFrame.Duration);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("h")]
    [InlineData("24")]
    [InlineData("24H")]
    [InlineData("1m")]
    [InlineData("1Y")]
    [InlineData("1D")]
    [InlineData("1W")]
    [InlineData("0h")]
    [InlineData("01h")]
    [InlineData("-5d")]
    [InlineData("+5d")]
    [InlineData("1.5d")]
    [InlineData("1 d")]
    [InlineData(" 1d")]
    [InlineData("1d ")]
    [InlineData("24hours")]
    [InlineData("abc")]
    [InlineData("99999999999d")]
    public void TryParse_RejectsAnythingElse_UnitsAreCaseSensitive(string? text)
    {
        Assert.False(TimeFrame.TryParse(text, out _, out var error));

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("366d")]
    [InlineData("13M")]
    [InlineData("53w")]
    [InlineData("2y")]
    [InlineData("8761h")]
    public void TryParse_RejectsAnythingLongerThanOneYear(string text)
    {
        Assert.False(TimeFrame.TryParse(text, out _, out var error));

        Assert.Equal("The time frame cannot be longer than one year.", error);
    }
}
