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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

/// <summary>The period to ask DevLake for: a time frame such as <c>24h</c> counted back from now, or an explicit UTC range. Exactly one is set.</summary>
public sealed record StatsQuery
{
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private StatsQuery(string? timeFrame, DateTimeOffset? from, DateTimeOffset? to)
    {
        TimeFrame = timeFrame;
        From = from;
        To = to;
    }

    public string? TimeFrame { get; }

    public DateTimeOffset? From { get; }

    public DateTimeOffset? To { get; }

    public static StatsQuery ForTimeFrame(string timeFrame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeFrame);
        return new StatsQuery(timeFrame, null, null);
    }

    public static StatsQuery ForRange(DateTimeOffset from, DateTimeOffset to)
    {
        return from < to ? new StatsQuery(null, from, to) : throw new ArgumentException("The range must end after it starts.", nameof(to));
    }

    /// <summary>The query string for DevLake's stats endpoints: <c>tf=24h</c>, or <c>from=...&amp;to=...</c> in UTC.</summary>
    public string ToQueryString()
    {
        return TimeFrame is not null
            ? $"tf={Uri.EscapeDataString(TimeFrame)}"
            : $"from={Uri.EscapeDataString(Format(From!.Value))}&to={Uri.EscapeDataString(Format(To!.Value))}";
    }

    private static string Format(DateTimeOffset instant) => instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);
}
