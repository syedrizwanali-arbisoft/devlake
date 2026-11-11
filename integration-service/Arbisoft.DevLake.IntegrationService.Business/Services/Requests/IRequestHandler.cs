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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>Accepts stats requests from clients: identifies the client, normalizes the emails, checks for duplicates, then logs and queues.</summary>
public interface IRequestHandler
{
    Task<IntakeResult<StatsAcknowledgement>> HandleSingleAsync(string clientId, StatsRequest request, CancellationToken cancellationToken);

    Task<IntakeResult<BatchStatsAcknowledgement>> HandleBatchAsync(string clientId, BatchStatsRequest request, CancellationToken cancellationToken);
}
