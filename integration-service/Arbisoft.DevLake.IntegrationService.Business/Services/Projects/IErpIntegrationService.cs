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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Projects;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Projects;

/// <summary>How a project fetch ended. The projects are only present when it succeeded.</summary>
public enum ProjectsFetchOutcome
{
    Fetched,

    /// <summary>Workstream refused this service's own token, so nothing was fetched.</summary>
    TokenInvalid
}

public sealed record ProjectsFetchResult(ProjectsFetchOutcome Outcome, IReadOnlyList<ProjectDto> Projects);

public interface IErpIntegrationService
{
    /// <summary>
    /// Checks Workstream's token, then reads every page of its projects and returns them as one list. If anything fails on any page, nothing is
    /// returned: a partial list would make DevLake believe projects had been removed.
    /// </summary>
    /// <exception cref="DataAccess.ExternalApis.ExternalApiException">Workstream could not be used.</exception>
    Task<ProjectsFetchResult> FetchProjectsAsync(string correlationId, CancellationToken cancellationToken);
}
