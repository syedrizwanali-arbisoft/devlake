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

public sealed class ClientBuilder
{
    private readonly Client _client = new()
    {
        Id = 1,
        ClientId = "sophia",
        Name = "Sophia",
        IsActive = true,
        AccessTokenLifetimeSeconds = 3600,
        CreatedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "test",
    };

    public ClientBuilder Inactive()
    {
        _client.IsActive = false;
        return this;
    }

    public ClientBuilder WithLifetime(int seconds)
    {
        _client.AccessTokenLifetimeSeconds = seconds;
        return this;
    }

    public Client Build() => _client;
}
