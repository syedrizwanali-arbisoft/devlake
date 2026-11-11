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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;

/// <summary>Calls Workstream, the source of project and team lead data.</summary>
public interface IWorkstreamClient
{
    /// <summary>Asks Workstream whether our token is valid. False means Workstream refused it; an unreachable Workstream throws instead.</summary>
    /// <exception cref="ExternalApiUnavailableException">Workstream could not be reached or kept failing.</exception>
    Task<bool> IsTokenValidAsync(string correlationId, CancellationToken cancellationToken);

    /// <summary>One page of projects. Pages start at 1. The caller keeps asking until <see cref="WorkstreamProjectsPage.IsLast"/>.</summary>
    /// <exception cref="ExternalAuthenticationException">Workstream rejected the token.</exception>
    /// <exception cref="ExternalApiUnavailableException">Workstream could not be reached or kept failing.</exception>
    /// <exception cref="ExternalApiResponseException">The answer did not contain a list of projects.</exception>
    Task<WorkstreamProjectsPage> GetProjectsPageAsync(int page, int pageSize, string correlationId, CancellationToken cancellationToken);
}
