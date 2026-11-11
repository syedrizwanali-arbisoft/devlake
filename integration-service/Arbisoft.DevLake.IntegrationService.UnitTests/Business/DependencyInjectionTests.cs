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
using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
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
    [InlineData("IntervalSeconds", "0")]
    [InlineData("IntervalSeconds", "3601")]
    [InlineData("MaxParallelRequests", "0")]
    [InlineData("MaxParallelRequests", "33")]
    [InlineData("DeliveryRetryDelaySeconds", "0")]
    [InlineData("StaleGraceSeconds", "-1")]
    public void ProcessingSettings_OutOfRange_FailValidation(string name, string value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Security:EncryptionKey"] = SecurityTestData.NewEncryptionKey(),
            ["Security:Issuer"] = SecurityTestData.Issuer,
            ["Security:Audience"] = SecurityTestData.Audience,
            [$"Processing:{name}"] = value
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddBusiness(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ProcessingOptions>>().Value);
    }

    [Fact]
    public void ProcessingDefaults_AreThirtySeconds_FourAtATime_AndAThirtySecondRetryWait()
    {
        var options = new ProcessingOptions();

        Assert.True(options.Enabled);
        Assert.Equal(30, options.IntervalSeconds);
        Assert.Equal(4, options.MaxParallelRequests);
        Assert.Equal(30, options.DeliveryRetryDelaySeconds);
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
