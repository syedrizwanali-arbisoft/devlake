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

/// <summary>The names of the HTTP clients, each with its own timeout, retry and circuit-breaker settings.</summary>
public static class ExternalHttpClients
{
    public const string DevLake = "devlake";
    public const string Workstream = "workstream";

    /// <summary>Fetching Sophia's access token. Safe to repeat, so it retries.</summary>
    public const string SophiaToken = "sophia-token";

    /// <summary>Posting results to Sophia's callback. Not retried here: the processing job retries and records every attempt, so a POST is never repeated blindly.</summary>
    public const string SophiaCallback = "sophia-callback";
}
