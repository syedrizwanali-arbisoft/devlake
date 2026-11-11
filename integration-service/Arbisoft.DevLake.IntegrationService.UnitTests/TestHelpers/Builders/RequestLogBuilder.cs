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

/// <summary>Builds a <see cref="RequestLog"/> for a single-engineer stats request, in the Success state by default.</summary>
public sealed class RequestLogBuilder
{
    private long _id = 1;
    private long _clientRefId = 1;
    private string _correlationId = Guid.NewGuid().ToString();
    private string _url = "/api/v1/engineers/{email}/stats?tf=24h";
    private string _status = RequestStatus.Success;
    private string _eventType = "engineer-stats";
    private DateTimeOffset _createdOn = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private string? _idempotencyKey;

    public RequestLogBuilder WithId(long id) => Set(() => _id = id);

    public RequestLogBuilder ForClient(long clientRefId) => Set(() => _clientRefId = clientRefId);

    public RequestLogBuilder WithCorrelationId(string correlationId) => Set(() => _correlationId = correlationId);

    public RequestLogBuilder WithStatus(string status) => Set(() => _status = status);

    public RequestLogBuilder Batch() => Set(() => (_eventType, _url) = ("engineer-stats-batch", "/api/v1/engineers/stats/batch"));

    public RequestLogBuilder CreatedOn(DateTimeOffset createdOn) => Set(() => _createdOn = createdOn);

    public RequestLogBuilder WithIdempotencyKey(string key) => Set(() => _idempotencyKey = key);

    public RequestLog Build() => new()
    {
        Id = _id,
        ClientRefId = _clientRefId,
        CorrelationId = _correlationId,
        Url = _url,
        Status = _status,
        EventType = _eventType,
        CreatedOn = _createdOn,
        IdempotencyKey = _idempotencyKey
    };

    private RequestLogBuilder Set(Action change)
    {
        change();
        return this;
    }
}
