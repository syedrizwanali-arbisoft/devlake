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

/// <summary>Calls DevLake's backend with its API token. DevLake works on a manually generated token, not OAuth2.</summary>
public interface IDevLakeClient
{
    /// <summary>Asks DevLake whether our API token is valid. False means DevLake refused it; an unreachable DevLake throws instead.</summary>
    /// <exception cref="ExternalApiUnavailableException">DevLake could not be reached or kept failing.</exception>
    Task<bool> IsTokenValidAsync(string correlationId, CancellationToken cancellationToken);

    /// <summary>One kind of stats for one email over a period. An email with no data comes back as empty stats, not an error.</summary>
    /// <exception cref="ExternalAuthenticationException">DevLake rejected the API token.</exception>
    /// <exception cref="ExternalApiUnavailableException">DevLake could not be reached or kept failing.</exception>
    /// <exception cref="ExternalApiRejectedException">DevLake refused this request.</exception>
    Task<JsonElement> GetStatsAsync(DevLakeStatsSource source, string email, StatsQuery query, string correlationId, CancellationToken cancellationToken);
}
