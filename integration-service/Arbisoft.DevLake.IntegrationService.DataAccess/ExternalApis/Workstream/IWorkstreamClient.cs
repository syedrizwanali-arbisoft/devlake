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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;

public interface IWorkstreamClient
{
    /// <summary>Calls Workstream's GET /is_valid. Returns false when Workstream rejects the token.</summary>
    Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every page of GET /projects and returns all items. Any failed page throws, so a caller never gets a partial
    /// list. Pages may be a JSON array, which ends with a short page, or an object with "results" and "next", which ends
    /// when "next" is null.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> GetAllProjectsAsync(CancellationToken cancellationToken = default);
}
