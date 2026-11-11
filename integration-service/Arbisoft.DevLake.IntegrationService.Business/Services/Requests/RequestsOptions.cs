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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>Settings for accepting stats requests. Every value can be changed in configuration without touching code.</summary>
public sealed class RequestsOptions
{
    public const string SectionName = "Requests";

    /// <summary>
    /// An identical request (same client, emails and time frame) made again within this many minutes is a duplicate and gets the original
    /// acknowledgement. The idempotency check reads this on every request, so a change to appsettings.json applies without a restart.
    /// </summary>
    [Range(1, 1440)]
    public int DuplicateWindowMinutes { get; init; } = 15;

    /// <summary>Only emails in these domains are accepted.</summary>
    [Required, MinLength(1)]
    public IReadOnlyList<string> AllowedEmailDomains { get; init; } = ["arbisoft.com"];
}
