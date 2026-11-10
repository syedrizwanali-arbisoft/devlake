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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>A time frame such as 24h, 1w, 30d, 11M or 1y. Units are case-sensitive: M is months.</summary>
public sealed partial record TimeFrame
{
    public const string FormatDescription = "a number from 1 to 999 followed by h, d, w, M or y, for example 24h";

    private TimeFrame(int amount, char unit)
    {
        Amount = amount;
        Unit = unit;
    }

    public int Amount { get; }
    public char Unit { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out TimeFrame? timeFrame)
    {
        timeFrame = null;
        if (value is null)
        {
            return false;
        }

        var match = Pattern().Match(value);
        if (!match.Success)
        {
            return false;
        }

        timeFrame = new TimeFrame(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value[0]);
        return true;
    }

    /// <summary>The start of the time frame that ends at <paramref name="end"/>.</summary>
    public DateTime StartBefore(DateTime end) => Unit switch
    {
        'h' => end.AddHours(-Amount),
        'd' => end.AddDays(-Amount),
        'w' => end.AddDays(-7 * Amount),
        'M' => end.AddMonths(-Amount),
        'y' => end.AddYears(-Amount),
        _ => throw new InvalidOperationException($"Unknown time frame unit '{Unit}'."),
    };

    public override string ToString() => Amount.ToString(CultureInfo.InvariantCulture) + Unit;

    [GeneratedRegex("^([1-9][0-9]{0,2})([hdwMy])$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
