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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Options;

/// <summary>
/// How calls to the upstream systems (DevLake, Sophia, Workstream) retry and when they stop. Each system's attempt timeout is set in its own
/// section. Only timeouts, 5xx and 429 are retried, never a 4xx (INT-5).
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>How many times a failed read call is retried after the first attempt.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; init; } = 3;

    /// <summary>The delay before the first retry. Later retries wait twice as long each time, with jitter so retries do not arrive together.</summary>
    [Range(1, 60_000)]
    public int RetryBaseDelayMilliseconds { get; init; } = 1000;

    /// <summary>The circuit opens when at least this share of calls in the sampling window failed.</summary>
    [Range(0.01, 1.0)]
    public double BreakerFailureRatio { get; init; } = 0.5;

    /// <summary>The breaker needs at least this many calls in the sampling window before it can open, so one failure on a quiet system does not trip it.</summary>
    [Range(2, 10_000)]
    public int BreakerMinimumThroughput { get; init; } = 10;

    [Range(1, 3600)]
    public int BreakerSamplingSeconds { get; init; } = 30;

    /// <summary>How long the circuit stays open before it lets one probe call through (half-open).</summary>
    [Range(1, 3600)]
    public int BreakerBreakSeconds { get; init; } = 30;
}
