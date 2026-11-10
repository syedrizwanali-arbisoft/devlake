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
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class RequestStatusService(
    IClientRepository clients,
    IRequestLogRepository requestLogs,
    TimeProvider timeProvider) : IRequestStatusService
{
    public static class PollStatus
    {
        public const string Queued = "Queued";
        public const string Processing = "Processing";
        public const string Success = "Success";
        public const string Failure = "Failure";
        public const string Revoked = "Revoked";
    }

    public async Task<RequestStatusResult> GetStatusAsync(string clientId, string? correlationId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(correlationId, out var id))
        {
            return new RequestStatusResult(RequestStatusOutcome.InvalidId, null);
        }

        // Stored IDs are lowercase "D" format, so an uppercase or braced ID from the client still matches.
        var normalizedId = id.ToString();
        var log = await requestLogs.GetByCorrelationIdAsync(normalizedId, cancellationToken);
        if (log is null)
        {
            return new RequestStatusResult(RequestStatusOutcome.NotFound, null);
        }

        var client = await clients.GetByClientIdAsync(clientId, cancellationToken);
        if (client is null || client.Id != log.ClientRefId)
        {
            return new RequestStatusResult(RequestStatusOutcome.NotFound, null);
        }

        var checkTime = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return new RequestStatusResult(RequestStatusOutcome.Found, new RequestStatusResponse(normalizedId, ToPollStatus(log.Status), checkTime));
    }

    public static string ToPollStatus(string status) => status switch
    {
        RequestLogStatus.Received or RequestLogStatus.Queued => PollStatus.Queued,
        RequestLogStatus.Processing or RequestLogStatus.Processed => PollStatus.Processing,
        RequestLogStatus.Success => PollStatus.Success,
        RequestLogStatus.Failed => PollStatus.Failure,
        RequestLogStatus.RevokedNoCallbackUrl => PollStatus.Revoked,
        _ => throw new InvalidOperationException($"Request log status '{status}' has no poll status."),
    };
}
