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

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>Readiness check: the broker accepts a connection and opens a channel.</summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;

    public RabbitMqHealthCheck(IRabbitMqConnection connection)
    {
        _connection = connection;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var channel = await _connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (QueueUnavailableException exception)
        {
            return HealthCheckResult.Unhealthy("The message broker is not reachable.", exception);
        }
    }
}
