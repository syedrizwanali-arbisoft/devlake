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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>
/// A publisher, consumer and connection wired to a real broker, on a queue that exists only for one test and is deleted afterwards.
/// Set <c>INTEGRATION_TEST_RABBITMQ</c> to <c>host=...;port=...;management=...;user=...;password=...</c>.
/// </summary>
public sealed class RabbitMqHarness : IAsyncDisposable
{
    public const string EnvironmentVariable = "INTEGRATION_TEST_RABBITMQ";

    private readonly HttpClient _management;

    private RabbitMqHarness(RabbitMqOptions options, Dictionary<string, string> settings)
    {
        Options = options;
        Connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(options), NullLogger<RabbitMqConnection>.Instance);
        Publisher = new RequestQueuePublisher(Connection, Microsoft.Extensions.Options.Options.Create(options));
        Consumer = new RequestQueueConsumer(Connection, Microsoft.Extensions.Options.Options.Create(options), NullLogger<RequestQueueConsumer>.Instance);
        _management = new HttpClient { BaseAddress = new Uri($"http://{settings["host"]}:{settings["management"]}/") };
        _management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings["user"]}:{settings["password"]}")));
    }

    public static Dictionary<string, string>? Settings => Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public RabbitMqOptions Options { get; }

    public RabbitMqConnection Connection { get; }

    public RequestQueuePublisher Publisher { get; }

    public RequestQueueConsumer Consumer { get; }

    public static RabbitMqHarness Create(int maxLength = 10_000, int ttlSeconds = 7200, int deliveryLimit = 5, int batchLimit = 10)
    {
        var settings = Settings ?? throw new InvalidOperationException($"{EnvironmentVariable} is not set.");
        var id = Guid.NewGuid().ToString("N");
        var options = new RabbitMqOptions
        {
            HostName = settings["host"],
            Port = int.Parse(settings["port"], System.Globalization.CultureInfo.InvariantCulture),
            UserName = settings["user"],
            Password = settings["password"],
            ConnectionName = $"it-{id}",
            RequestQueueName = $"it.{id}",
            QueueMaxLength = maxLength,
            MessageTtlSeconds = ttlSeconds,
            DeliveryLimit = deliveryLimit,
            ReceiveBatchLimit = batchLimit
        };
        return new RabbitMqHarness(options, settings);
    }

    public static QueueMessage NewMessage(string? json = null) => new(Guid.NewGuid().ToString("D"), "engineer-stats", json ?? "{\"email\":\"a@arbisoft.com\",\"timeFrame\":\"24h\"}");

    public string DeadQueue => RabbitMqTopology.DeadLetterQueue(Options);

    /// <summary>Messages ready in a queue (not counting ones delivered but not yet acknowledged).</summary>
    public async Task<uint> CountAsync(string queue)
    {
        await using var channel = await Connection.CreateChannelAsync(false, CancellationToken.None);
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    /// <summary>Reads one message straight from the broker, bypassing the consumer's checks.</summary>
    public async Task<BasicGetResult?> RawGetAsync(string queue)
    {
        var channel = await Connection.CreateChannelAsync(false, CancellationToken.None);
        return await channel.BasicGetAsync(queue, autoAck: true);
    }

    /// <summary>Publishes a hand-made message, so a test can send what the real publisher never would.</summary>
    public async Task RawPublishAsync(BasicProperties properties, string body = "{}")
    {
        await using var channel = await Connection.CreateChannelAsync(false, CancellationToken.None);
        await channel.BasicPublishAsync(string.Empty, Options.RequestQueueName, mandatory: true, properties, Encoding.UTF8.GetBytes(body));
    }

    public async Task<JsonElement> QueueInfoAsync()
    {
        return await _management.GetFromJsonAsync<JsonElement>($"api/queues/%2F/{Uri.EscapeDataString(Options.RequestQueueName)}");
    }

    /// <summary>Closes this harness's own connection from the broker side, as a broker restart or network drop would.</summary>
    public async Task DropConnectionFromTheBrokerAsync()
    {
        // The management API refreshes its connection list every few seconds, so wait for ours to appear.
        string? name = null;
        await WaitUntilAsync(
            async () =>
            {
                var connections = await _management.GetFromJsonAsync<JsonElement>("api/connections");
                name = connections.EnumerateArray().FirstOrDefault(c => c.GetProperty("user_provided_name").GetString() == Options.ConnectionName)
                    is { ValueKind: JsonValueKind.Object } found ? found.GetProperty("name").GetString() : null;
                return name is not null;
            },
            timeoutSeconds: 30);
        using var response = await _management.DeleteAsync($"api/connections/{Uri.EscapeDataString(name!)}");
        response.EnsureSuccessStatusCode();
        await WaitUntilAsync(async () => (await _management.GetFromJsonAsync<JsonElement>("api/connections"))
            .EnumerateArray().All(c => c.GetProperty("user_provided_name").GetString() != Options.ConnectionName), timeoutSeconds: 30);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The condition was not met in time.");
    }

    /// <summary>Deletes a test queue and its dead-letter queue and exchange. Does nothing when no test broker is configured or reachable.</summary>
    public static async Task DeleteTopologyAsync(string queueName)
    {
        var settings = Settings;
        if (settings is null)
        {
            return;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = settings["host"],
                Port = int.Parse(settings["port"], System.Globalization.CultureInfo.InvariantCulture),
                UserName = settings["user"],
                Password = settings["password"]
            };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeleteAsync(queueName);
            await channel.QueueDeleteAsync(queueName + QueueConstants.DeadLetterQueueSuffix);
            await channel.ExchangeDeleteAsync(queueName + QueueConstants.DeadLetterExchangeSuffix);
        }
        catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException)
        {
            // The broker is gone, so there is nothing left to clean up.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DeleteTopologyAsync(Options.RequestQueueName);
        await Publisher.DisposeAsync();
        await Connection.DisposeAsync();
        _management.Dispose();
    }

    private static Dictionary<string, string>? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
