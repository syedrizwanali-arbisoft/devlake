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
using MySqlConnector;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

[Collection(MySqlTestGroup.Name)]
public class TokenRepositoryTests : RepositoryTestBase
{
    public TokenRepositoryTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
    }

    private async Task<AccessToken> NewTokenAsync(string? jti = null, DateTimeOffset? issuedOn = null)
    {
        var client = await InsertClientAsync();
        return new AccessToken
        {
            Jti = jti ?? Guid.NewGuid().ToString("D"),
            ClientRefId = client.Id,
            ClientSecretId = await InsertSecretAsync(client.Id),
            SigningKeyId = await InsertSigningKeyAsync(),
            GrantedScopes = "stats:read projects:fetch",
            IssuedOn = issuedOn ?? Instant,
            ExpiresOn = Instant.AddHours(1),
            IssuedToIp = "203.0.113.7"
        };
    }

    [MySqlFact]
    public async Task AccessToken_InsertGetByJti_AndRevokeOnlyOnce()
    {
        var repository = Get<IAccessTokenRepository>();
        var token = await NewTokenAsync();
        var id = await repository.InsertAsync(token, CancellationToken.None);

        var found = await repository.GetByJtiAsync(token.Jti, CancellationToken.None);
        Assert.Equal(id, found!.Id);
        Assert.Equal("stats:read projects:fetch", found.GrantedScopes);
        Assert.Equal(Instant.AddHours(1), found.ExpiresOn);
        Assert.Null(found.RevokedOn);

        Assert.True(await repository.RevokeAsync(id, "leaked", Instant.AddMinutes(5), CancellationToken.None));
        Assert.False(await repository.RevokeAsync(id, "again", Instant.AddMinutes(6), CancellationToken.None));

        var revoked = await repository.GetByIdAsync(id, CancellationToken.None);
        Assert.Equal(Instant.AddMinutes(5), revoked!.RevokedOn);
        Assert.Equal("leaked", revoked.RevokedReason);
    }

    [MySqlFact]
    public async Task AccessToken_DuplicateJti_IsRejected()
    {
        var repository = Get<IAccessTokenRepository>();
        var first = await NewTokenAsync();
        await repository.InsertAsync(first, CancellationToken.None);

        var second = await NewTokenAsync(first.Jti);
        var exception = await Assert.ThrowsAsync<MySqlException>(() => repository.InsertAsync(second, CancellationToken.None));

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [MySqlFact]
    public async Task AccessToken_List_FiltersByClientAndOrdersNewestFirst()
    {
        var repository = Get<IAccessTokenRepository>();
        var older = await NewTokenAsync(issuedOn: Instant);
        var olderId = await repository.InsertAsync(older, CancellationToken.None);
        var newer = new AccessToken
        {
            Jti = Guid.NewGuid().ToString("D"),
            ClientRefId = older.ClientRefId,
            ClientSecretId = older.ClientSecretId,
            SigningKeyId = older.SigningKeyId,
            GrantedScopes = older.GrantedScopes,
            IssuedOn = Instant.AddMinutes(10),
            ExpiresOn = Instant.AddHours(1),
            IssuedToIp = older.IssuedToIp
        };
        var newerId = await repository.InsertAsync(newer, CancellationToken.None);

        var page = await repository.ListAsync(older.ClientRefId, 1, 10, CancellationToken.None);

        Assert.Equal([newerId, olderId], page.Items.Select(t => t.Id));
        Assert.Equal(2, page.TotalCount);
    }

    [MySqlFact]
    public async Task SigningKey_GetActive_SkipsRetiredAndFutureKeys_ButGetByKidFindsRetiredOnes()
    {
        var repository = Get<ISigningKeyRepository>();
        var future = await InsertSigningKeyAsync(activatedOn: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var oldKid = Unique("old");
        var oldId = await InsertSigningKeyAsync(oldKid, new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var currentKid = Unique("current");
        var currentId = await InsertSigningKeyAsync(currentKid, new DateTimeOffset(2090, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var at = new DateTimeOffset(2091, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(currentId, (await repository.GetActiveAsync(at, CancellationToken.None))!.Id);

        Assert.True(await repository.RetireAsync(currentId, at, CancellationToken.None));
        Assert.False(await repository.RetireAsync(currentId, at, CancellationToken.None));
        Assert.Equal(oldId, (await repository.GetActiveAsync(at, CancellationToken.None))!.Id);
        Assert.Equal(at, (await repository.GetByKidAsync(currentKid, CancellationToken.None))!.RetiredOn);
        Assert.NotEqual(future, (await repository.GetActiveAsync(at, CancellationToken.None))!.Id);
    }
}
