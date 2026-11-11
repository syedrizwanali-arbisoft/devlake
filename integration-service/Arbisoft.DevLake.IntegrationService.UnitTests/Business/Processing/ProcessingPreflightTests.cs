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

using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public class ProcessingPreflightTests
{
    private readonly IDevLakeClient _devLake = Substitute.For<IDevLakeClient>();
    private readonly ISophiaClient _sophia = Substitute.For<ISophiaClient>();
    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly ProcessingPreflight _preflight;

    public ProcessingPreflightTests()
    {
        _devLake.IsTokenValidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _logs.GetClientsWithPendingRequestsAsync(Arg.Any<CancellationToken>()).Returns([7L, 8L]);
        _sophia.EnsureAccessTokenAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _preflight = new ProcessingPreflight(_devLake, _sophia, _logs);
    }

    [Fact]
    public async Task WhenDevLakeAndEverySophiaClientAreFine_ProcessingMayGoAhead()
    {
        var result = await _preflight.RunAsync(CancellationToken.None);

        Assert.True(result.Ready);
        Assert.Null(result.Problem);
        await _sophia.Received(1).EnsureAccessTokenAsync(7, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sophia.Received(1).EnsureAccessTokenAsync(8, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenDevLakeRefusesTheToken_ProcessingStops_AndSophiaIsNotBotheredAboutIt()
    {
        _devLake.IsTokenValidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _preflight.RunAsync(CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Contains("DevLake rejected", result.Problem, StringComparison.Ordinal);
        await _sophia.DidNotReceiveWithAnyArgs().EnsureAccessTokenAsync(default, default!, default);
    }

    [Theory]
    [InlineData(typeof(ExternalApiUnavailableException))]
    [InlineData(typeof(ExternalAuthenticationException))]
    public async Task WhenDevLakeCannotBeChecked_ProcessingStops_WithTheReason(Type exceptionType)
    {
        _devLake.IsTokenValidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(exceptionType == typeof(ExternalApiUnavailableException)
                ? new ExternalApiUnavailableException("DevLake", "DevLake is down")
                : new ExternalAuthenticationException("DevLake", "DevLake token problem")));

        var result = await _preflight.RunAsync(CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Contains("DevLake could not be checked", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenNoSophiaTokenCanBeGot_ProcessingStops()
    {
        _sophia.EnsureAccessTokenAsync(8, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new ExternalAuthenticationException("Sophia", "Sophia refused to issue an access token (401).", 401)));

        var result = await _preflight.RunAsync(CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Contains("Sophia access token", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableSophia_StopsProcessing_Too()
    {
        _sophia.EnsureAccessTokenAsync(7, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new ExternalApiUnavailableException("Sophia", "down")));

        Assert.False((await _preflight.RunAsync(CancellationToken.None)).Ready);
    }

    [Fact]
    public async Task AClientWithAnIncompleteConfig_DoesNotStopEveryoneElse()
    {
        _sophia.EnsureAccessTokenAsync(7, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new CallbackNotConfiguredException("incomplete")));

        var result = await _preflight.RunAsync(CancellationToken.None);

        Assert.True(result.Ready);
        await _sophia.Received(1).EnsureAccessTokenAsync(8, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoClientsHavingRequestsWaiting_OnlyDevLakeMatters()
    {
        _logs.GetClientsWithPendingRequestsAsync(Arg.Any<CancellationToken>()).Returns([]);

        Assert.True((await _preflight.RunAsync(CancellationToken.None)).Ready);
        await _sophia.DidNotReceiveWithAnyArgs().EnsureAccessTokenAsync(default, default!, default);
    }
}
