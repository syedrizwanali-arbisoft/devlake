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

/// <summary>Settings for calling Workstream, the source of project and team lead data.</summary>
public sealed class WorkstreamOptions
{
    public const string SectionName = "Workstream";

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string ApiToken { get; init; } = string.Empty;

    [Range(1, 100)]
    public int PageSize { get; init; } = 10;

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 30;
}
