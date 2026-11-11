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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Gives each test its own DI scope on the shared test database, plus helpers that insert the rows other tables point at.</summary>
public abstract class RepositoryTestBase : IAsyncLifetime
{
    /// <summary>A UTC instant with microsecond precision, which is what DATETIME(6) keeps.</summary>
    protected static readonly DateTimeOffset Instant = new DateTimeOffset(2026, 11, 11, 12, 30, 45, TimeSpan.Zero).AddTicks(1234560);

    private readonly MySqlDatabaseFixture _fixture;
    private AsyncServiceScope _scope;

    protected RepositoryTestBase(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        if (_fixture.Services is not null)
        {
            _scope = _fixture.Services.CreateAsyncScope();
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
    }

    protected T Get<T>()
        where T : notnull
    {
        return _scope.ServiceProvider.GetRequiredService<T>();
    }

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected async Task<Client> InsertClientAsync(string? clientId = null)
    {
        var client = new Client
        {
            ClientId = clientId ?? Unique("client"),
            Name = "Test client",
            IsActive = true,
            AccessTokenLifetimeSeconds = 3600,
            CreatedOn = Instant,
            CreatedBy = "tests"
        };
        var id = await Get<IClientRepository>().InsertAsync(client, CancellationToken.None);
        return (await Get<IClientRepository>().GetByIdAsync(id, CancellationToken.None))!;
    }

    protected async Task<long> InsertSecretAsync(long clientRefId)
    {
        var secret = new ClientSecret { ClientRefId = clientRefId, Secret = "hash", CreatedOn = Instant, CreatedBy = "tests" };
        return await Get<IClientRepository>().InsertSecretAsync(secret, CancellationToken.None);
    }

    protected async Task<long> InsertSigningKeyAsync(string? kid = null, DateTimeOffset? activatedOn = null)
    {
        var key = new SigningKey
        {
            Kid = kid ?? Unique("kid"),
            Algorithm = "RS256",
            PublicKey = "public",
            PrivateKeyRef = "ref",
            ActivatedOn = activatedOn ?? Instant,
            CreatedOn = Instant
        };
        return await Get<ISigningKeyRepository>().InsertAsync(key, CancellationToken.None);
    }

    protected async Task<long> InsertRequestLogAsync(long clientRefId, string status = RequestStatus.Received, DateTimeOffset? createdOn = null)
    {
        var log = new RequestLog
        {
            ClientRefId = clientRefId,
            CorrelationId = Guid.NewGuid().ToString("D"),
            Url = "/api/v1/engineers/a@arbisoft.com/stats?tf=24h",
            Status = status,
            EventType = "engineer-stats",
            CreatedOn = createdOn ?? Instant
        };
        return await Get<IRequestLogRepository>().InsertAsync(log, CancellationToken.None);
    }
}
