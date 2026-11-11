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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;

/// <summary>
/// The period part of the single-email stats request: <c>tf=24h</c>, or <c>from=...&amp;to=...</c>. Which combinations are allowed is checked
/// by <see cref="StatsPeriod"/>, so the rule lives in one place.
/// </summary>
public sealed class StatsPeriodQuery : IValidatableObject
{
    /// <summary>A time frame such as 24h, 1w, 30d, 11M or 1y, counted back from now.</summary>
    [FromQuery(Name = "tf")]
    public string? TimeFrame { get; init; }

    /// <summary>The start of a range: an ISO 8601 timestamp with an offset, for example 2026-11-01T00:00:00Z.</summary>
    [FromQuery(Name = "from")]
    public string? From { get; init; }

    /// <summary>The end of a range, in the same format. It must be later than <see cref="From"/>.</summary>
    [FromQuery(Name = "to")]
    public string? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!StatsPeriod.TryParse(TimeFrame, From, To, out _, out var error))
        {
            yield return new ValidationResult(error, [ErrorMember()]);
        }
    }

    /// <summary>Names the query parameter the error is about, so the client sees which one to fix.</summary>
    private string ErrorMember()
    {
        if (!string.IsNullOrEmpty(TimeFrame) || (string.IsNullOrEmpty(From) && string.IsNullOrEmpty(To)))
        {
            return "tf";
        }

        return string.IsNullOrEmpty(To) ? "to" : "from";
    }
}
