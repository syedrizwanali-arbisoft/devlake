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

namespace Arbisoft.DevLake.IntegrationService.API.RateLimiting;

/// <summary>Per-client request limits, per minute. The defaults are the limits agreed with Sophia; they can be changed in configuration.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Single-email stats requests per client per minute.</summary>
    [Range(1, 100_000)]
    public int StatsPerMinute { get; init; } = 100;

    /// <summary>Batch stats requests per client per minute.</summary>
    [Range(1, 100_000)]
    public int BatchPerMinute { get; init; } = 20;

    /// <summary>Request status poll requests per client per minute.</summary>
    [Range(1, 100_000)]
    public int PollPerMinute { get; init; } = 100;
}
