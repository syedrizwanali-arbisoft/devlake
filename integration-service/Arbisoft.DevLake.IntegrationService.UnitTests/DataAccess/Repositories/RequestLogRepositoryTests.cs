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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

[Collection(MySqlTestGroup.Name)]
public class RequestLogRepositoryTests : RepositoryTestBase
{
    public RequestLogRepositoryTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
    }

    private IRequestLogRepository Repository => Get<IRequestLogRepository>();

    [MySqlFact]
    public async Task Insert_GetByCorrelationId_AndJsonBodyRoundTrip()
    {
        var client = await InsertClientAsync();
        var id = await InsertRequestLogAsync(client.Id);
        var log = (await Repository.GetByIdAsync(id, CancellationToken.None))!;
        await Repository.InsertJsonAsync(new RequestLogJson { RequestLogId = id, Json = "{\"email\":\"a@arbisoft.com\",\"timeFrame\":\"24h\"}" }, CancellationToken.None);

        var byCorrelation = await Repository.GetByCorrelationIdAsync(log.CorrelationId, CancellationToken.None);
        var json = await Repository.GetJsonAsync(id, CancellationToken.None);

        Assert.Equal(id, byCorrelation!.Id);
        Assert.Equal(RequestStatus.Received, byCorrelation.Status);
        Assert.Equal(36, byCorrelation.CorrelationId.Length);
        Assert.Contains("\"timeFrame\"", json, StringComparison.Ordinal);
        Assert.Null(await Repository.GetJsonAsync(long.MaxValue, CancellationToken.None));
    }

    [MySqlFact]
    public async Task Insert_DuplicateCorrelationId_IsRejected()
    {
        var client = await InsertClientAsync();
        var log = new RequestLog
        {
            ClientRefId = client.Id,
            CorrelationId = Guid.NewGuid().ToString("D"),
            Url = "/x",
            Status = RequestStatus.Received,
            EventType = "engineer-stats",
            CreatedOn = Instant
        };
        await Repository.InsertAsync(log, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<MySqlException>(() => Repository.InsertAsync(log, CancellationToken.None));

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [MySqlFact]
    public async Task UpdateStatus_AndListByStatusSince_ReturnOldestFirstWithinTheWindow()
    {
        var client = await InsertClientAsync();
        var since = Instant.AddDays(3650);
        var late = await InsertRequestLogAsync(client.Id, RequestStatus.Queued, since.AddMinutes(10));
        var early = await InsertRequestLogAsync(client.Id, RequestStatus.Queued, since.AddMinutes(1));
        await InsertRequestLogAsync(client.Id, RequestStatus.Queued, since.AddMinutes(-1));
        var received = await InsertRequestLogAsync(client.Id, RequestStatus.Received, since.AddMinutes(2));
        Assert.True(await Repository.UpdateStatusAsync(received, RequestStatus.Queued, CancellationToken.None));

        var queued = await Repository.ListByStatusSinceAsync(RequestStatus.Queued, since, 10, CancellationToken.None);
        var limited = await Repository.ListByStatusSinceAsync(RequestStatus.Queued, since, 2, CancellationToken.None);

        Assert.Equal([early, received, late], queued.Select(l => l.Id));
        Assert.Equal(2, limited.Count);
    }

    [MySqlFact]
    public async Task List_AppliesEachFilterIndependently()
    {
        var client = await InsertClientAsync();
        var other = await InsertClientAsync();
        var day = Instant.AddDays(7300);
        var wanted = await InsertRequestLogAsync(client.Id, RequestStatus.Failed, day);
        await InsertRequestLogAsync(client.Id, RequestStatus.Success, day);
        await InsertRequestLogAsync(other.Id, RequestStatus.Failed, day);
        await InsertRequestLogAsync(client.Id, RequestStatus.Failed, day.AddDays(2));

        var page = await Repository.ListAsync(new RequestLogFilter(client.Id, RequestStatus.Failed, day.AddHours(-1), day.AddDays(1)), 1, 10, CancellationToken.None);
        var all = await Repository.ListAsync(new RequestLogFilter(client.Id), 1, 10, CancellationToken.None);

        Assert.Equal(wanted, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalCount);
        Assert.True(all.TotalCount >= 3);
    }

    private static RequestLog WithKey(long clientRefId, string? key, string status = RequestStatus.Received) => new()
    {
        ClientRefId = clientRefId,
        CorrelationId = Guid.NewGuid().ToString("D"),
        Url = "/x",
        Status = status,
        EventType = "engineer-stats",
        CreatedOn = Instant,
        IdempotencyKey = key
    };

    [MySqlFact]
    public async Task TryInsert_AcceptsTheFirstRequestWithAKey_AndRefusesASecondWithoutThrowing()
    {
        var client = await InsertClientAsync();
        var key = Guid.NewGuid().ToString("N");

        var first = await Repository.TryInsertAsync(WithKey(client.Id, key), CancellationToken.None);
        var second = await Repository.TryInsertAsync(WithKey(client.Id, key), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(first, (await Repository.GetByIdempotencyKeyAsync(key, CancellationToken.None))!.Id);
    }

    [MySqlFact]
    public async Task TryInsert_ManyRequestsWithoutAKey_NeverCollide()
    {
        var client = await InsertClientAsync();

        Assert.NotNull(await Repository.TryInsertAsync(WithKey(client.Id, null), CancellationToken.None));
        Assert.NotNull(await Repository.TryInsertAsync(WithKey(client.Id, null), CancellationToken.None));
        await Repository.InsertAsync(WithKey(client.Id, null, RequestStatus.RevokedNoCallbackUrl), CancellationToken.None);
    }

    [MySqlFact]
    public async Task TryInsert_ARacingPair_LetsExactlyOneWin()
    {
        var client = await InsertClientAsync();
        var key = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = NewScope();
            return await scope.ServiceProvider.GetRequiredService<IRequestLogRepository>().TryInsertAsync(WithKey(client.Id, key), CancellationToken.None);
        }));

        Assert.Single(results, id => id is not null);
        Assert.Equal(7, results.Count(id => id is null));
    }

    [MySqlFact]
    public async Task ReleaseIdempotencyKey_FreesTheKeyOfThatRowOnly_SoARepeatCanTakeIt()
    {
        var client = await InsertClientAsync();
        var key = Guid.NewGuid().ToString("N");
        var old = (await Repository.TryInsertAsync(WithKey(client.Id, key), CancellationToken.None))!.Value;

        await Repository.ReleaseIdempotencyKeyAsync(old, CancellationToken.None);
        var replacement = await Repository.TryInsertAsync(WithKey(client.Id, key), CancellationToken.None);
        await Repository.ReleaseIdempotencyKeyAsync(old, CancellationToken.None);

        Assert.NotNull(replacement);
        Assert.Null((await Repository.GetByIdAsync(old, CancellationToken.None))!.IdempotencyKey);
        Assert.Equal(replacement, (await Repository.GetByIdempotencyKeyAsync(key, CancellationToken.None))!.Id);
    }

    [MySqlFact]
    public async Task GetByIdempotencyKey_UnknownKey_ReturnsNull()
    {
        Assert.Null(await Repository.GetByIdempotencyKeyAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));
    }

    [MySqlFact]
    public async Task Attempts_AreRecordedInOrder_WithTheErrorOfAFailedAttempt()
    {
        var client = await InsertClientAsync();
        var id = await InsertRequestLogAsync(client.Id);
        await Repository.InsertAttemptAsync(new RequestResponseAttempt
        {
            RequestLogId = id,
            ResponseJson = "{\"error\":\"boom\"}",
            AttemptStatus = "Failed",
            FailureError = "HTTP 500",
            AttemptBy = "RequestProcessor",
            AttemptOn = Instant
        }, CancellationToken.None);
        await Repository.InsertAttemptAsync(new RequestResponseAttempt
        {
            RequestLogId = id,
            ResponseJson = "{}",
            AttemptStatus = "Success",
            AttemptBy = "RequestProcessor",
            AttemptOn = Instant.AddSeconds(30)
        }, CancellationToken.None);

        var attempts = await Repository.GetAttemptsAsync(id, CancellationToken.None);

        Assert.Equal(["Failed", "Success"], attempts.Select(a => a.AttemptStatus));
        Assert.Equal("HTTP 500", attempts[0].FailureError);
        Assert.Null(attempts[1].FailureError);
    }
}
