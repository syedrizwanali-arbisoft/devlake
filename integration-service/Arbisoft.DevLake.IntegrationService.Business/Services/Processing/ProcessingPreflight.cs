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

using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class ProcessingPreflight : IProcessingPreflight
{
    private readonly IDevLakeClient _devLake;
    private readonly ISophiaClient _sophia;
    private readonly IRequestLogRepository _requestLogs;

    public ProcessingPreflight(IDevLakeClient devLake, ISophiaClient sophia, IRequestLogRepository requestLogs)
    {
        _devLake = devLake;
        _sophia = sophia;
        _requestLogs = requestLogs;
    }

    public async Task<PreflightResult> RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        return await CheckDevLakeAsync(runId, cancellationToken) ?? await CheckSophiaAsync(runId, cancellationToken) ?? PreflightResult.Go;
    }

    private async Task<PreflightResult?> CheckDevLakeAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            return await _devLake.IsTokenValidAsync(runId, cancellationToken) ? null : PreflightResult.Stop("DevLake rejected our API token.");
        }
        catch (ExternalApiException exception)
        {
            return PreflightResult.Stop($"DevLake could not be checked: {exception.Message}");
        }
    }

    private async Task<PreflightResult?> CheckSophiaAsync(string runId, CancellationToken cancellationToken)
    {
        foreach (var clientRefId in await _requestLogs.GetClientsWithPendingRequestsAsync(cancellationToken))
        {
            try
            {
                await _sophia.EnsureAccessTokenAsync(clientRefId, runId, cancellationToken);
            }
            catch (CallbackNotConfiguredException)
            {
                // A client with no usable outbound config is handled per request, as revoked. It does not stop everyone else.
            }
            catch (ExternalApiException exception)
            {
                return PreflightResult.Stop($"No Sophia access token for a client: {exception.Message}");
            }
        }

        return null;
    }
}
