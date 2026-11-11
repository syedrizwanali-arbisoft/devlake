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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Messaging;

/// <summary>The publisher and consumer against a real RabbitMQ broker. Each test gets its own queue, deleted afterwards.</summary>
public class RabbitMqBrokerTests
{
    [RabbitMqFact]
    public async Task PublishThenReceive_ReturnsTheMessageIntact()
    {
        await using var broker = RabbitMqHarness.Create();
        var message = RabbitMqHarness.NewMessage();

        await broker.Publisher.PublishAsync(message, CancellationToken.None);
        await using var batch = await broker.Consumer.ReceiveAsync(10, CancellationToken.None);

        var delivery = Assert.Single(batch.Deliveries);
        Assert.Equal(message, delivery.Message);
        Assert.False(delivery.Redelivered);
    }

    [RabbitMqFact]
    public async Task APublishedMessage_IsPersistent_Versioned_AndCarriesTheCorrelationId()
    {
        await using var broker = RabbitMqHarness.Create();
        var message = RabbitMqHarness.NewMessage();
        await broker.Publisher.PublishAsync(message, CancellationToken.None);

        var raw = await broker.RawGetAsync(broker.Options.RequestQueueName);

        Assert.NotNull(raw);
        Assert.True(raw.BasicProperties.Persistent);
        Assert.Equal(message.CorrelationId, raw.BasicProperties.CorrelationId);
        Assert.Equal(message.CorrelationId, raw.BasicProperties.MessageId);
        Assert.Equal("engineer-stats", raw.BasicProperties.Type);
        Assert.Equal("application/json", raw.BasicProperties.ContentType);
        Assert.Equal(1, raw.BasicProperties.Headers![QueueConstants.SchemaVersionHeader]);
    }

    [RabbitMqFact]
    public async Task TheQueue_IsDurable_Quorum_AndConfiguredWithTheAgreedLimitsAndDeadLetterExchange()
    {
        await using var broker = RabbitMqHarness.Create(maxLength: 123, ttlSeconds: 456);
        await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);

        var info = await broker.QueueInfoAsync();
        var arguments = info.GetProperty("arguments");

        Assert.True(info.GetProperty("durable").GetBoolean());
        Assert.Equal("quorum", info.GetProperty("type").GetString());
        Assert.Equal(122, arguments.GetProperty("x-max-length").GetInt32());
        Assert.Equal("reject-publish", arguments.GetProperty("x-overflow").GetString());
        Assert.Equal(456_000, arguments.GetProperty("x-message-ttl").GetInt32());
        Assert.Equal($"{broker.Options.RequestQueueName}.dlx", arguments.GetProperty("x-dead-letter-exchange").GetString());
    }

    [RabbitMqFact]
    public async Task AnAcknowledgedMessage_IsGone()
    {
        await using var broker = RabbitMqHarness.Create();
        await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);

        await using (var batch = await broker.Consumer.ReceiveAsync(10, CancellationToken.None))
        {
            await batch.AckAsync(batch.Deliveries[0], CancellationToken.None);
        }

        await using var again = await broker.Consumer.ReceiveAsync(10, CancellationToken.None);
        Assert.Empty(again.Deliveries);
        Assert.Equal(0u, await broker.CountAsync(broker.DeadQueue));
    }

    [RabbitMqFact]
    public async Task GetDepth_CountsTheWaitingMessages_WithoutTakingOneOrRaisingItsDeliveryCount()
    {
        await using var broker = RabbitMqHarness.Create();
        Assert.Equal(0, await broker.Consumer.GetDepthAsync(CancellationToken.None));
        for (var i = 0; i < 3; i++)
        {
            await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);
        }

        Assert.Equal(3, await broker.Consumer.GetDepthAsync(CancellationToken.None));
        Assert.Equal(3, await broker.Consumer.GetDepthAsync(CancellationToken.None));

        await using var batch = await broker.Consumer.ReceiveAsync(1, CancellationToken.None);
        var delivery = Assert.Single(batch.Deliveries);
        Assert.False(delivery.Redelivered);
        Assert.Equal(0, delivery.DeliveryCount);
        Assert.Equal(2, await broker.Consumer.GetDepthAsync(CancellationToken.None));
    }

    [RabbitMqFact]
    public async Task AMessageNeverAcknowledged_ComesBackAfterTheBatchIsDisposed()
    {
        await using var broker = RabbitMqHarness.Create();
        var message = RabbitMqHarness.NewMessage();
        await broker.Publisher.PublishAsync(message, CancellationToken.None);

        await using (var first = await broker.Consumer.ReceiveAsync(10, CancellationToken.None))
        {
            Assert.Single(first.Deliveries);
        }

        await using var second = await broker.Consumer.ReceiveAsync(10, CancellationToken.None);
        var delivery = Assert.Single(second.Deliveries);
        Assert.Equal(message, delivery.Message);
        Assert.True(delivery.Redelivered);
    }

    [RabbitMqFact]
    public async Task ARequeuedMessage_IsDeliveredAgainWithARisingDeliveryCount()
    {
        await using var broker = RabbitMqHarness.Create();
        await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);

        await using (var first = await broker.Consumer.ReceiveAsync(10, CancellationToken.None))
        {
            await first.RequeueAsync(first.Deliveries[0], CancellationToken.None);
        }

        await using var second = await broker.Consumer.ReceiveAsync(10, CancellationToken.None);
        var delivery = Assert.Single(second.Deliveries);
        Assert.True(delivery.Redelivered);
        Assert.True(delivery.DeliveryCount >= 1);
    }

    [RabbitMqFact]
    public async Task ADeadLetteredMessage_LandsInTheDeadLetterQueue_ForInspection()
    {
        await using var broker = RabbitMqHarness.Create();
        var message = RabbitMqHarness.NewMessage();
        await broker.Publisher.PublishAsync(message, CancellationToken.None);

        await using (var batch = await broker.Consumer.ReceiveAsync(10, CancellationToken.None))
        {
            await batch.DeadLetterAsync(batch.Deliveries[0], CancellationToken.None);
        }

        await RabbitMqHarness.WaitUntilAsync(async () => await broker.CountAsync(broker.DeadQueue) == 1);
        var dead = await broker.RawGetAsync(broker.DeadQueue);
        Assert.Equal(message.CorrelationId, dead!.BasicProperties.CorrelationId);
        Assert.Equal(0u, await broker.CountAsync(broker.Options.RequestQueueName));
    }

    [RabbitMqFact]
    public async Task MessagesTheCodeCannotRead_AreDeadLettered_NotReturnedAndNotLost()
    {
        await using var broker = RabbitMqHarness.Create();
        await broker.RawPublishAsync(new BasicProperties { CorrelationId = "a", Type = "t", Headers = new Dictionary<string, object?> { [QueueConstants.SchemaVersionHeader] = 99 } });
        await broker.RawPublishAsync(new BasicProperties { CorrelationId = "b", Type = "t" });
        await broker.RawPublishAsync(new BasicProperties { Headers = new Dictionary<string, object?> { [QueueConstants.SchemaVersionHeader] = 1 } });

        await using var batch = await broker.Consumer.ReceiveAsync(10, CancellationToken.None);

        Assert.Empty(batch.Deliveries);
        await RabbitMqHarness.WaitUntilAsync(async () => await broker.CountAsync(broker.DeadQueue) == 3);
    }

    [RabbitMqFact]
    public async Task Receive_NeverReturnsMoreThanThePerRunLimit_AndLeavesTheRestQueued()
    {
        await using var broker = RabbitMqHarness.Create(batchLimit: 10);
        for (var i = 0; i < 15; i++)
        {
            await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);
        }

        await using (var batch = await broker.Consumer.ReceiveAsync(100, CancellationToken.None))
        {
            Assert.Equal(10, batch.Deliveries.Count);
            foreach (var delivery in batch.Deliveries)
            {
                await batch.AckAsync(delivery, CancellationToken.None);
            }
        }

        Assert.Equal(5u, await broker.CountAsync(broker.Options.RequestQueueName));
    }

    [RabbitMqFact]
    public async Task AFullQueue_RejectsThePublish_NeverDropsSilently_AndAcceptsAgainOnceThereIsRoom()
    {
        await using var broker = RabbitMqHarness.Create(maxLength: 3);
        for (var i = 0; i < 3; i++)
        {
            await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);
        }

        await Assert.ThrowsAsync<QueueFullException>(() => broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None));
        Assert.Equal(3u, await broker.CountAsync(broker.Options.RequestQueueName));
        Assert.Equal(0u, await broker.CountAsync(broker.DeadQueue));

        await using (var batch = await broker.Consumer.ReceiveAsync(2, CancellationToken.None))
        {
            foreach (var delivery in batch.Deliveries)
            {
                await batch.AckAsync(delivery, CancellationToken.None);
            }
        }

        // The broker settles the acknowledgements asynchronously, so room appears a moment later.
        await RabbitMqHarness.WaitUntilAsync(async () =>
        {
            try
            {
                await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);
                return true;
            }
            catch (QueueFullException)
            {
                return false;
            }
        });
        Assert.Equal(2u, await broker.CountAsync(broker.Options.RequestQueueName));
    }

    [RabbitMqFact]
    public async Task AMessageThatKeepsFailing_IsDeliveredUpToTheLimit_ThenMovedToTheDeadLetterQueue()
    {
        await using var broker = RabbitMqHarness.Create(deliveryLimit: 2);
        var message = RabbitMqHarness.NewMessage();
        await broker.Publisher.PublishAsync(message, CancellationToken.None);

        for (var delivery = 1; delivery <= 3; delivery++)
        {
            await using var batch = await broker.Consumer.ReceiveAsync(1, CancellationToken.None);
            Assert.Equal(message, Assert.Single(batch.Deliveries).Message);
            await batch.RequeueAsync(batch.Deliveries[0], CancellationToken.None);
        }

        await using var last = await broker.Consumer.ReceiveAsync(1, CancellationToken.None);
        Assert.Empty(last.Deliveries);
        await RabbitMqHarness.WaitUntilAsync(async () => await broker.CountAsync(broker.DeadQueue) == 1);
        Assert.Equal(0u, await broker.CountAsync(broker.Options.RequestQueueName));
    }

    [RabbitMqFact]
    public async Task AMessageThatWaitsTooLong_ExpiresIntoTheDeadLetterQueue()
    {
        await using var broker = RabbitMqHarness.Create(ttlSeconds: 1);
        await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);

        await RabbitMqHarness.WaitUntilAsync(async () => await broker.CountAsync(broker.DeadQueue) == 1, timeoutSeconds: 20);
        Assert.Equal(0u, await broker.CountAsync(broker.Options.RequestQueueName));
    }

    [RabbitMqFact]
    public async Task ManyConcurrentPublishes_AllArrive()
    {
        await using var broker = RabbitMqHarness.Create();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None)));

        Assert.Equal(50u, await broker.CountAsync(broker.Options.RequestQueueName));
    }

    [RabbitMqFact]
    public async Task AfterTheBrokerDropsTheConnection_PublishingAndReceivingWorkAgain()
    {
        await using var broker = RabbitMqHarness.Create();
        await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);

        await broker.DropConnectionFromTheBrokerAsync();
        await broker.Publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None);
        await broker.DropConnectionFromTheBrokerAsync();

        await using var batch = await broker.Consumer.ReceiveAsync(10, CancellationToken.None);
        Assert.Equal(2, batch.Deliveries.Count);
    }

    [RabbitMqFact]
    public async Task WhenTheBrokerCannotBeReached_PublishingThrowsQueueUnavailable_AndReadinessIsUnhealthy()
    {
        var settings = RabbitMqHarness.Settings!;
        var unreachable = new RabbitMqOptions
        {
            HostName = settings["host"],
            Port = 1,
            UserName = "x",
            Password = "x",
            ConnectionName = "it-unreachable"
        };
        await using var connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(unreachable), Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMqConnection>.Instance);
        await using var publisher = new RequestQueuePublisher(connection, Microsoft.Extensions.Options.Options.Create(unreachable));

        await Assert.ThrowsAsync<QueueUnavailableException>(() => publisher.PublishAsync(RabbitMqHarness.NewMessage(), CancellationToken.None));
        var health = await new RabbitMqHealthCheck(connection).CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, health.Status);
    }

    [RabbitMqFact]
    public async Task Readiness_IsHealthy_WhenTheBrokerIsUp()
    {
        await using var broker = RabbitMqHarness.Create();

        var health = await new RabbitMqHealthCheck(broker.Connection).CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, health.Status);
    }
}
