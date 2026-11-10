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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

public enum DevLakeStatsCategory
{
    Jira,
    Github,
    AiUsage,
    AiCost,
}

public interface IDevLakeClient
{
    /// <summary>Calls DevLake's GET /is_valid. Returns false when DevLake rejects the token.</summary>
    Task<bool> IsTokenValidAsync(string? correlationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls GET /stats/{email}/{category}?tf=. Each category has its own response, documented in the README and in
    /// DevLake's Swagger spec (framework/integration). The JSON is returned as is, so the callback carries every field.
    /// </summary>
    Task<JsonElement> GetStatsAsync(
        string email, DevLakeStatsCategory category, string timeFrame, string correlationId, CancellationToken cancellationToken = default);
}
