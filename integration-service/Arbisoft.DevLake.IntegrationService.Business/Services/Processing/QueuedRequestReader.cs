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

using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

/// <summary>A queued message that has been read and checked.</summary>
public sealed record ReadableRequest(QueuedStatsRequest Request, StatsQuery Query);

/// <summary>
/// Reads the JSON body of a queued request. Fields it does not know are ignored, so a newer API can add fields without breaking an older job
/// (INT-M3). A body that cannot be read, or that does not say which emails and which period, is reported with a reason and never guessed at (MAP-9).
/// </summary>
public static class QueuedRequestReader
{
    public static bool TryRead(string json, out ReadableRequest? readable, out string? error)
    {
        readable = null;
        QueuedStatsRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<QueuedStatsRequest>(json);
        }
        catch (JsonException)
        {
            error = "The queued request is not valid JSON.";
            return false;
        }

        if (request is null || request.Emails is not { Count: > 0 } || request.Emails.Any(string.IsNullOrWhiteSpace))
        {
            error = "The queued request has no emails.";
            return false;
        }

        if (!StatsPeriod.TryParse(request.TimeFrame, request.From, request.To, out var period, out error))
        {
            return false;
        }

        var query = period!.TimeFrame is { } timeFrame ? StatsQuery.ForTimeFrame(timeFrame.Text) : StatsQuery.ForRange(period.From!.Value, period.To!.Value);
        readable = new ReadableRequest(request, query);
        return true;
    }
}
