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

using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Repositories;

[Collection(MySqlTestGroup.Name)]
public class SecretStoreRepositoryTests : RepositoryTestBase
{
    public SecretStoreRepositoryTests(MySqlDatabaseFixture fixture)
        : base(fixture)
    {
    }

    private ISecretStoreRepository Repository => Get<ISecretStoreRepository>();

    [MySqlFact]
    public async Task Save_InsertsThenReplacesTheValueForTheSameReference()
    {
        var secretRef = Unique("key");

        await Repository.SaveEncryptedValueAsync(secretRef, "v1.first", Instant, CancellationToken.None);
        await Repository.SaveEncryptedValueAsync(secretRef, "v1.second", Instant.AddMinutes(1), CancellationToken.None);

        Assert.Equal("v1.second", await Repository.GetEncryptedValueAsync(secretRef, CancellationToken.None));
    }

    [MySqlFact]
    public async Task Get_UnknownReference_ReturnsNull()
    {
        Assert.Null(await Repository.GetEncryptedValueAsync(Unique("missing"), CancellationToken.None));
    }
}
