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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

/// <summary>Settings for the background job that takes stats requests off the queue and delivers their results.</summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>Turns the job off, for a host that should only accept requests (or while investigating a problem). Requests stay queued.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the job looks at the queue.</summary>
    [Range(1, 3600)]
    public int IntervalSeconds { get; init; } = 30;

    /// <summary>The most requests processed at the same time in one run (ASY-5). DevLake is separately capped by <c>DevLake:MaxConcurrentRequests</c>.</summary>
    [Range(1, 32)]
    public int MaxParallelRequests { get; init; } = 4;

    /// <summary>The wait before the second attempt to deliver to Sophia. Each further wait doubles.</summary>
    [Range(1, 3600)]
    public int DeliveryRetryDelaySeconds { get; init; } = 30;

    /// <summary>How long past the queue's message lifetime a request may stay unfinished before it is marked failed.</summary>
    [Range(0, 3600)]
    public int StaleGraceSeconds { get; init; } = 60;
}
