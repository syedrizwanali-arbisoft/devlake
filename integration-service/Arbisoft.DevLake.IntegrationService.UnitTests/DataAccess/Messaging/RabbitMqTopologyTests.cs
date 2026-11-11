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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

public class RabbitMqTopologyTests
{
    private static readonly RabbitMqOptions Options = new() { RequestQueueName = "integration.stats-requests" };

    [Fact]
    public void RequestQueue_IsAQuorumQueue_ThatHoldsExactlyTheConfiguredMaximum_AndExpiresMessages()
    {
        var arguments = RabbitMqTopology.RequestQueueArguments(Options);

        Assert.Equal("quorum", arguments["x-queue-type"]);
        Assert.Equal(9_999, arguments["x-max-length"]);
        Assert.Equal("reject-publish", arguments["x-overflow"]);
        Assert.Equal(7_200_000, arguments["x-message-ttl"]);
        Assert.False(arguments.ContainsKey("x-delivery-limit"));
    }

    [Fact]
    public void RequestQueue_DeadLettersToAnExchangeNamedAfterIt()
    {
        var arguments = RabbitMqTopology.RequestQueueArguments(Options);

        Assert.Equal("integration.stats-requests.dlx", arguments["x-dead-letter-exchange"]);
        Assert.Equal("integration.stats-requests.dlx", RabbitMqTopology.DeadLetterExchange(Options));
        Assert.Equal("integration.stats-requests.dead", RabbitMqTopology.DeadLetterQueue(Options));
    }

    [Fact]
    public void Limits_FollowTheOptions()
    {
        var custom = new RabbitMqOptions { QueueMaxLength = 3, MessageTtlSeconds = 60, DeliveryLimit = 2 };

        var arguments = RabbitMqTopology.RequestQueueArguments(custom);

        Assert.Equal(2, arguments["x-max-length"]);
        Assert.Equal(60_000, arguments["x-message-ttl"]);
    }
}
