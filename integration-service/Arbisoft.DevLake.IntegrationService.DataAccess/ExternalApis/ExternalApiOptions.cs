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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

public sealed class DevLakeOptions
{
    public const string SectionName = "DevLake";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>DevLake's manually generated API token. Set it through the DevLake__ApiToken environment variable.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>The time allowed for a call, retries included.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class WorkstreamOptions
{
    public const string SectionName = "Workstream";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Set it through the Workstream__ApiToken environment variable.</summary>
    public string ApiToken { get; set; } = string.Empty;

    public int PageSize { get; set; } = 10;

    /// <summary>A guard against an API that never reports its last page.</summary>
    public int MaxPages { get; set; } = 1000;

    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class SophiaOptions
{
    public const string SectionName = "Sophia";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>A token is renewed this long before it expires.</summary>
    public int TokenRenewalMarginSeconds { get; set; } = 60;
}
