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
using System.Text.RegularExpressions;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>
/// The period a stats request covers: either a time frame counted back from now (<c>24h</c>), or an explicit range between two instants.
/// Exactly one of the two is set. Range endpoints are UTC at second precision, and a period is never longer than one year (API-5).
/// </summary>
public sealed partial class StatsPeriod
{
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private static readonly string[] AcceptedInstantFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    private StatsPeriod(TimeFrame? timeFrame, DateTimeOffset? from, DateTimeOffset? to)
    {
        TimeFrame = timeFrame;
        From = from;
        To = to;
    }

    public TimeFrame? TimeFrame { get; }

    public DateTimeOffset? From { get; }

    public DateTimeOffset? To { get; }

    /// <summary>A stable text form used in the idempotency key, so equal periods always give the same text.</summary>
    public string Canonical => TimeFrame is { } timeFrame ? $"tf:{timeFrame.Text}" : $"range:{FormatInstant(From!.Value)}/{FormatInstant(To!.Value)}";

    /// <summary>An instant as the queue and the logs carry it: UTC, second precision, with a trailing Z.</summary>
    public static string FormatInstant(DateTimeOffset instant) => instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the period from the three query values. Give <paramref name="timeFrame"/>, or both <paramref name="from"/> and <paramref name="to"/>;
    /// anything else is an error.
    /// </summary>
    public static bool TryParse(string? timeFrame, string? from, string? to, out StatsPeriod? period, out string? error)
    {
        period = null;
        var hasTimeFrame = !string.IsNullOrEmpty(timeFrame);
        var hasFrom = !string.IsNullOrEmpty(from);
        var hasTo = !string.IsNullOrEmpty(to);

        if (hasTimeFrame && (hasFrom || hasTo))
        {
            error = "Provide either tf, or both from and to, not both.";
            return false;
        }

        if (hasTimeFrame)
        {
            if (!Services.Requests.TimeFrame.TryParse(timeFrame, out var parsed, out error))
            {
                return false;
            }

            period = new StatsPeriod(parsed, null, null);
            return true;
        }

        if (!hasFrom && !hasTo)
        {
            error = "Provide tf, or both from and to.";
            return false;
        }

        return TryParseRange(from, to, out period, out error);
    }

    private static bool TryParseRange(string? from, string? to, out StatsPeriod? period, out string? error)
    {
        period = null;
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            error = "from and to must be given together.";
            return false;
        }

        if (!TryParseInstant(from, out var start) || !TryParseInstant(to, out var end))
        {
            error = "from and to must be ISO 8601 timestamps with a time and an offset, for example 2026-11-01T00:00:00Z.";
            return false;
        }

        if (start >= end)
        {
            error = "from must be earlier than to.";
            return false;
        }

        if (end - start > Services.Requests.TimeFrame.MaxDuration)
        {
            error = "The range cannot be longer than one year.";
            return false;
        }

        period = new StatsPeriod(null, start, end);
        error = null;
        return true;
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();

    private static bool TryParseInstant(string text, out DateTimeOffset utc)
    {
        // The offset must be written out. Without it the text would be read in the server's local time zone, so the same request would mean
        // different instants depending on where the service runs.
        if (!ExplicitOffset().IsMatch(text)
            || !DateTimeOffset.TryParseExact(text, AcceptedInstantFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            utc = default;
            return false;
        }

        var instant = parsed.ToUniversalTime();
        utc = instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerSecond));
        return true;
    }
}
