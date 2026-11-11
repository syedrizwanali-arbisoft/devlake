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

using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class CallbackAvailabilityTests
{
    private readonly IClientConfigRepository _configs = Substitute.For<IClientConfigRepository>();
    private readonly CallbackAvailability _availability;

    public CallbackAvailabilityTests()
    {
        _availability = new CallbackAvailability(_configs);
    }

    private void Given(bool configActive, bool urlExists)
    {
        _configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(new ClientConfig { Id = 3, ClientRefId = 7, IsActive = configActive });
        _configs.GetActiveOutboundUrlAsync(3, "engineer-stats", Arg.Any<CancellationToken>()).Returns(urlExists ? new ClientConfigOutboundUrl { Id = 9 } : null);
    }

    [Fact]
    public async Task AnActiveConfigWithAnActiveUrl_IsAvailable()
    {
        Given(configActive: true, urlExists: true);

        Assert.True(await _availability.IsAvailableAsync(7, "engineer-stats", CancellationToken.None));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task AnInactiveConfig_OrNoActiveUrl_IsNotAvailable(bool configActive, bool urlExists)
    {
        Given(configActive, urlExists);

        Assert.False(await _availability.IsAvailableAsync(7, "engineer-stats", CancellationToken.None));
    }

    [Fact]
    public async Task AClientWithNoConfigAtAll_IsNotAvailable_WithoutLookingForUrls()
    {
        _configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns((ClientConfig?)null);

        Assert.False(await _availability.IsAvailableAsync(7, "engineer-stats", CancellationToken.None));

        await _configs.DidNotReceiveWithAnyArgs().GetActiveOutboundUrlAsync(default, default!, default);
    }

    [Fact]
    public async Task EachEventType_IsCheckedOnItsOwn()
    {
        _configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(new ClientConfig { Id = 3, ClientRefId = 7, IsActive = true });
        _configs.GetActiveOutboundUrlAsync(3, "engineer-stats", Arg.Any<CancellationToken>()).Returns(new ClientConfigOutboundUrl { Id = 9 });
        _configs.GetActiveOutboundUrlAsync(3, "engineer-stats-batch", Arg.Any<CancellationToken>()).Returns((ClientConfigOutboundUrl?)null);

        Assert.True(await _availability.IsAvailableAsync(7, "engineer-stats", CancellationToken.None));
        Assert.False(await _availability.IsAvailableAsync(7, "engineer-stats-batch", CancellationToken.None));
    }
}
