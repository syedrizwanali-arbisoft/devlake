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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";

    /// <summary>Set it through the RabbitMq__Username environment variable.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Set it through the RabbitMq__Password environment variable.</summary>
    public string Password { get; set; } = string.Empty;

    public string RequestQueue { get; set; } = string.Empty;

    /// <summary>How long a publish may wait for the broker's confirmation.</summary>
    public int PublishTimeoutSeconds { get; set; } = 10;

    /// <summary>Messages that fail twice are dead-lettered here, so they are kept for inspection or replay.</summary>
    public string FailedQueue => RequestQueue + ".failed";
}
