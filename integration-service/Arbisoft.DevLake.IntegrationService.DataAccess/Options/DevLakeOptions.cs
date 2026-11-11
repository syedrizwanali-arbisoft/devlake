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

/// <summary>Settings for calling DevLake. DevLake works on a manually generated API token, not OAuth2.</summary>
public sealed class DevLakeOptions
{
    public const string SectionName = "DevLake";

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string ApiToken { get; init; } = string.Empty;

    /// <summary>How long one attempt waits for DevLake before it counts as timed out.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>The most requests in flight to DevLake at once, across the whole process. It protects DevLake's database from a large batch (ASY-7).</summary>
    [Range(1, 100)]
    public int MaxConcurrentRequests { get; init; } = 8;
}
