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

    [Required]
    public string RequestQueueName { get; init; } = "integration.stats-requests";
}
