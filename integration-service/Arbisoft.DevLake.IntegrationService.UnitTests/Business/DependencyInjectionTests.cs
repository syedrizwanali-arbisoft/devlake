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

using Arbisoft.DevLake.IntegrationService.Business;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business;

public class DependencyInjectionTests
{
    private static ServiceProvider Build(string? encryptionKey)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Security:EncryptionKey"] = encryptionKey,
            ["Security:Issuer"] = SecurityTestData.Issuer,
            ["Security:Audience"] = SecurityTestData.Audience
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddBusiness(configuration).BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddBusiness_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddBusiness(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void AddBusiness_WithAValidKey_ResolvesTheSecurityServices()
    {
        using var provider = Build(SecurityTestData.NewEncryptionKey());

        Assert.NotNull(provider.GetRequiredService<ISecretEncryptor>());
        Assert.NotNull(provider.GetRequiredService<IClientSecretHasher>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public void AddBusiness_WithABadMasterKey_FailsValidation(string key)
    {
        using var provider = Build(key);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SecurityOptions>>().Value);
    }
}
