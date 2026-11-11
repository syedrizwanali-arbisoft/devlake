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

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;

/// <summary>A queued single-email stats request from client 1.</summary>
public sealed class RequestLogBuilder
{
    public const string DefaultCorrelationId = "3eef2f41-18a6-45e6-b620-e11aa9c69812";

    private readonly RequestLog _log = new()
    {
        Id = 42,
        ClientRefId = 1,
        CorrelationId = DefaultCorrelationId,
        Url = "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h",
        Status = RequestLogStatus.Queued,
        EventType = "engineer-stats",
        RequestHash = "hash",
        CreatedOn = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc),
    };

    public RequestLogBuilder WithId(long id)
    {
        _log.Id = id;
        return this;
    }

    public RequestLogBuilder WithCorrelationId(string correlationId)
    {
        _log.CorrelationId = correlationId;
        return this;
    }

    public RequestLogBuilder WithStatus(string status)
    {
        _log.Status = status;
        return this;
    }

    public RequestLogBuilder ForClient(long clientRefId)
    {
        _log.ClientRefId = clientRefId;
        return this;
    }

    public RequestLog Build() => _log;
}
