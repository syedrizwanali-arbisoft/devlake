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

using System.Globalization;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>
/// A requested time frame such as <c>24h</c>, <c>1w</c>, <c>30d</c>, <c>11M</c> or <c>1y</c>. Units are case-sensitive: h hours, d days,
/// w weeks, M months, y years. Months count as 30 days and years as 365. The longest accepted time frame is one year (API-5).
/// </summary>
public readonly record struct TimeFrame
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(365);

    private const int MaxDigits = 5;

    private TimeFrame(int value, char unit, string text)
    {
        Value = value;
        Unit = unit;
        Text = text;
    }

    public int Value { get; }

    public char Unit { get; }

    /// <summary>The time frame as the caller wrote it, for example <c>24h</c>.</summary>
    public string Text { get; }

    public TimeSpan Duration => Unit switch
    {
        'h' => TimeSpan.FromHours(Value),
        'd' => TimeSpan.FromDays(Value),
        'w' => TimeSpan.FromDays(7.0 * Value),
        'M' => TimeSpan.FromDays(30.0 * Value),
        _ => TimeSpan.FromDays(365.0 * Value)
    };

    public static bool TryParse(string? text, out TimeFrame timeFrame, out string? error)
    {
        timeFrame = default;
        if (string.IsNullOrEmpty(text) || text.Length < 2 || text.Length > MaxDigits + 1)
        {
            error = "The time frame must be a number followed by a unit, for example 24h, 1w, 30d, 11M or 1y.";
            return false;
        }

        var unit = text[^1];
        if (unit is not ('h' or 'd' or 'w' or 'M' or 'y'))
        {
            error = "The time frame unit must be one of h, d, w, M or y (case-sensitive).";
            return false;
        }

        var digits = text[..^1];
        if (digits[0] == '0' || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            error = "The time frame must start with a whole number greater than zero.";
            return false;
        }

        var candidate = new TimeFrame(value, unit, text);
        if (candidate.Duration > MaxDuration)
        {
            error = "The time frame cannot be longer than one year.";
            return false;
        }

        timeFrame = candidate;
        error = null;
        return true;
    }
}
