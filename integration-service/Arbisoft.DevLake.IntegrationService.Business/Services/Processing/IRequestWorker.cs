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

using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

public interface IRequestWorker
{
    /// <summary>
    /// Does everything for one queued request: collects the stats, delivers them, and leaves the request log in its final state. A shutdown
    /// (<paramref name="stopping"/>) puts the request back instead of abandoning it half done.
    /// </summary>
    Task<WorkerOutcome> ProcessAsync(QueueDelivery delivery, CancellationToken stopping);
}
