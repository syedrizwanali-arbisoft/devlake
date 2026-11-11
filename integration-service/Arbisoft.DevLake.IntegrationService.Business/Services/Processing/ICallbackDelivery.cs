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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public interface ICallbackDelivery
{
    /// <summary>
    /// Posts a request's result to the client's callback, trying up to the client's <c>maxAttempts</c> times with a growing wait between tries, recording
    /// every try, and leaving the request Success, Failed or Revoked. The post itself is never cut short by a shutdown, so a delivered result is
    /// always recorded; the wait between tries is.
    /// </summary>
    Task<WorkerOutcome> DeliverAsync(RequestLog request, string payloadJson, CancellationToken stopping);
}
