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

using System.Globalization;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class RequestStatusService : IRequestStatusService
{
    private const string CheckTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string CorrelationIdFormat = "D";

    private readonly IClientRepository _clients;
    private readonly IRequestLogRepository _requestLogs;
    private readonly TimeProvider _timeProvider;

    public RequestStatusService(IClientRepository clients, IRequestLogRepository requestLogs, TimeProvider timeProvider)
    {
        _clients = clients;
        _requestLogs = requestLogs;
        _timeProvider = timeProvider;
    }

    public async Task<RequestStatusResponse?> GetStatusAsync(string clientId, Guid correlationId, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByClientIdAsync(clientId, cancellationToken);
        if (client is null)
        {
            return null;
        }

        var log = await _requestLogs.GetByCorrelationIdAsync(correlationId.ToString(CorrelationIdFormat), cancellationToken);
        if (log is null || log.ClientRefId != client.Id)
        {
            return null;
        }

        var checkTime = _timeProvider.GetUtcNow().UtcDateTime.ToString(CheckTimeFormat, CultureInfo.InvariantCulture);
        return new RequestStatusResponse(log.CorrelationId, PollStatus.From(log.Status), checkTime);
    }
}
