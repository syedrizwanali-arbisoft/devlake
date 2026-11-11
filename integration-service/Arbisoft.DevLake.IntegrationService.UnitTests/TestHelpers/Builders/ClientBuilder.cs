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

/// <summary>Builds a <see cref="Client"/> with sensible defaults, so a test states only what it cares about.</summary>
public sealed class ClientBuilder
{
    private long _id = 1;
    private string _clientId = "sophia";
    private string _name = "Sophia";
    private bool _isActive = true;
    private int _lifetimeSeconds = 900;
    private DateTimeOffset _createdOn = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private string _createdBy = "admin";
    private DateTimeOffset? _modifiedOn;
    private string? _modifiedBy;

    public ClientBuilder WithId(long id) => Set(() => _id = id);

    public ClientBuilder WithClientId(string clientId) => Set(() => _clientId = clientId);

    public ClientBuilder WithName(string name) => Set(() => _name = name);

    public ClientBuilder Inactive() => Set(() => _isActive = false);

    public ClientBuilder WithLifetime(int seconds) => Set(() => _lifetimeSeconds = seconds);

    public ClientBuilder CreatedOn(DateTimeOffset createdOn, string createdBy = "admin") => Set(() => (_createdOn, _createdBy) = (createdOn, createdBy));

    public ClientBuilder ModifiedOn(DateTimeOffset modifiedOn, string modifiedBy) => Set(() => (_modifiedOn, _modifiedBy) = (modifiedOn, modifiedBy));

    public Client Build() => new()
    {
        Id = _id,
        ClientId = _clientId,
        Name = _name,
        IsActive = _isActive,
        AccessTokenLifetimeSeconds = _lifetimeSeconds,
        CreatedOn = _createdOn,
        CreatedBy = _createdBy,
        ModifiedOn = _modifiedOn,
        ModifiedBy = _modifiedBy
    };

    private ClientBuilder Set(Action change)
    {
        change();
        return this;
    }
}
