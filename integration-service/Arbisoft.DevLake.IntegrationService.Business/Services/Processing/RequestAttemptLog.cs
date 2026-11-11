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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public sealed class RequestAttemptLog : IRequestAttemptLog
{
    private readonly IRequestLogRepository _requestLogs;
    private readonly TimeProvider _timeProvider;

    public RequestAttemptLog(IRequestLogRepository requestLogs, TimeProvider timeProvider)
    {
        _requestLogs = requestLogs;
        _timeProvider = timeProvider;
    }

    public async Task RecordAsync(long requestLogId, string attemptStatus, string responseJson, string? failure, CancellationToken cancellationToken)
    {
        await _requestLogs.InsertAttemptAsync(
            new RequestResponseAttempt
            {
                RequestLogId = requestLogId,
                ResponseJson = responseJson,
                AttemptStatus = attemptStatus,
                FailureError = failure,
                AttemptBy = ProcessingConstants.AttemptBy,
                AttemptOn = _timeProvider.GetUtcNow()
            },
            cancellationToken);
    }

    public async Task<int> CountDeliveryAttemptsAsync(long requestLogId, CancellationToken cancellationToken)
    {
        var attempts = await _requestLogs.GetAttemptsAsync(requestLogId, cancellationToken);
        return attempts.Count(attempt => attempt.AttemptStatus is DeliveryAttemptStatus.Delivered or DeliveryAttemptStatus.DeliveryFailed);
    }
}
