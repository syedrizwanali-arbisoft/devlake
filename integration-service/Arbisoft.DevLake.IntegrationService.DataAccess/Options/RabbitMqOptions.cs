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

using System.ComponentModel.DataAnnotations;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Options;

/// <summary>Settings for the RabbitMQ broker that holds queued stats requests.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    [Required]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string VirtualHost { get; init; } = "/";

    /// <summary>Encrypt the connection to the broker. Turn this on for any broker that is not on localhost.</summary>
    public bool UseTls { get; init; }

    /// <summary>The name this service's connection shows in the broker's management UI.</summary>
    [Required]
    public string ConnectionName { get; init; } = "integration-service";

    /// <summary>The queue stats requests wait in. Its dead-letter exchange and queue are named after it.</summary>
    [Required]
    public string RequestQueueName { get; init; } = "integration.stats-requests";

    /// <summary>The most messages the queue holds. When it is full the broker rejects new publishes and the API answers 429.</summary>
    [Range(2, int.MaxValue)]
    public int QueueMaxLength { get; init; } = 10_000;

    /// <summary>How long a message may wait before it expires into the dead-letter queue.</summary>
    [Range(1, int.MaxValue)]
    public int MessageTtlSeconds { get; init; } = 7200;

    /// <summary>How many times a failing message is redelivered before the consumer moves it to the dead-letter queue.</summary>
    [Range(1, 1000)]
    public int DeliveryLimit { get; init; } = 5;

    /// <summary>The most messages one receive call returns. This bounds the work in flight per run (the pull-based prefetch).</summary>
    [Range(1, 1000)]
    public int ReceiveBatchLimit { get; init; } = 10;
}
