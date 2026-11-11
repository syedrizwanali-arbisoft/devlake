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

using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Services.Projects;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Projects;

public class ErpIntegrationServiceTests
{
    private const string CorrelationId = "0b8f3c52-1111-4222-8333-444455556666";

    private readonly IWorkstreamClient _workstream = Substitute.For<IWorkstreamClient>();
    private readonly ErpIntegrationService _service;

    public ErpIntegrationServiceTests()
    {
        _workstream.IsTokenValidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _service = new ErpIntegrationService(_workstream, Options.Create(new WorkstreamOptions { PageSize = 3 }), NullLogger<ErpIntegrationService>.Instance);
    }

    private static JsonElement Item(int id) => JsonDocument.Parse($"{{\"id\":\"p{id}\",\"name\":\"Project {id}\",\"team_lead\":{{\"name\":\"Lead {id}\"}}}}").RootElement.Clone();

    private static WorkstreamProjectsPage Page(int page, params int[] ids) => new(ids.Select(Item).ToList(), page, 3);

    private void GivenPages(params WorkstreamProjectsPage[] pages)
    {
        foreach (var page in pages)
        {
            _workstream.GetProjectsPageAsync(page.Page, 3, CorrelationId, Arg.Any<CancellationToken>()).Returns(page);
        }
    }

    [Fact]
    public async Task EveryPage_IsReadAndMergedInOrder_UntilTheShortOne()
    {
        GivenPages(Page(1, 1, 2, 3), Page(2, 4, 5, 6), Page(3, 7));

        var result = await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        Assert.Equal(ProjectsFetchOutcome.Fetched, result.Outcome);
        Assert.Equal(["p1", "p2", "p3", "p4", "p5", "p6", "p7"], result.Projects.Select(p => p.Id));
        Assert.Equal("Lead 4", result.Projects[3].TeamLead!.Name);
        await _workstream.Received(1).GetProjectsPageAsync(3, 3, CorrelationId, Arg.Any<CancellationToken>());
        await _workstream.DidNotReceive().GetProjectsPageAsync(4, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePageSizeComesFromTheWorkstreamSettings()
    {
        GivenPages(Page(1, 1));

        await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        await _workstream.Received(1).GetProjectsPageAsync(1, 3, CorrelationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheTokenIsCheckedFirst_AndARefusedTokenFetchesNothing()
    {
        _workstream.IsTokenValidAsync(CorrelationId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        Assert.Equal(ProjectsFetchOutcome.TokenInvalid, result.Outcome);
        Assert.Empty(result.Projects);
        await _workstream.DidNotReceiveWithAnyArgs().GetProjectsPageAsync(default, default, default!, default);
    }

    [Fact]
    public async Task TheTokenIsCheckedBeforeAnyPageIsRead()
    {
        GivenPages(Page(1, 1));

        await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        Received.InOrder(() =>
        {
            _workstream.IsTokenValidAsync(CorrelationId, Arg.Any<CancellationToken>());
            _workstream.GetProjectsPageAsync(1, 3, CorrelationId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AnEmptyList_IsAnEmptyResult_NotAnError()
    {
        GivenPages(Page(1));

        var result = await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        Assert.Equal(ProjectsFetchOutcome.Fetched, result.Outcome);
        Assert.Empty(result.Projects);
    }

    [Theory]
    [InlineData(typeof(ExternalApiUnavailableException))]
    [InlineData(typeof(ExternalAuthenticationException))]
    [InlineData(typeof(ExternalApiResponseException))]
    [InlineData(typeof(ExternalApiRejectedException))]
    public async Task AFailureOnALaterPage_FailsTheWholeCall_SoNoPartialListIsReturned(Type failure)
    {
        GivenPages(Page(1, 1, 2, 3));
        _workstream.GetProjectsPageAsync(2, 3, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WorkstreamProjectsPage>(Failure(failure)));

        await Assert.ThrowsAsync(failure, () => _service.FetchProjectsAsync(CorrelationId, CancellationToken.None));
    }

    [Fact]
    public async Task AProjectMovingBetweenPages_IsListedOnce()
    {
        GivenPages(Page(1, 1, 2, 3), Page(2, 3, 4, 5), Page(3, 5));

        var result = await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        Assert.Equal(["p1", "p2", "p3", "p4", "p5"], result.Projects.Select(p => p.Id));
    }

    [Fact]
    public async Task AWorkstreamThatIgnoresPaging_IsStopped_WhenAPageAddsNothingNew()
    {
        _workstream.GetProjectsPageAsync(Arg.Any<int>(), 3, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(call => Page(call.ArgAt<int>(0), 1, 2, 3));

        var result = await _service.FetchProjectsAsync(CorrelationId, CancellationToken.None);

        Assert.Equal(["p1", "p2", "p3"], result.Projects.Select(p => p.Id));
        await _workstream.Received(2).GetProjectsPageAsync(Arg.Any<int>(), 3, CorrelationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWorkstreamThatNeverEnds_IsStoppedAtThePageLimit_WithAnError()
    {
        var next = 0;
        _workstream.GetProjectsPageAsync(Arg.Any<int>(), 3, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(call => Page(call.ArgAt<int>(0), next++, next++, next++));

        var exception = await Assert.ThrowsAsync<ExternalApiResponseException>(() => _service.FetchProjectsAsync(CorrelationId, CancellationToken.None));

        Assert.Contains("more pages", exception.Message, StringComparison.Ordinal);
        await _workstream.Received(ErpIntegrationService.MaxPages).GetProjectsPageAsync(Arg.Any<int>(), 3, CorrelationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AProjectThatCannotBeRead_FailsTheCall_NamingThePageButNeverTheData()
    {
        var bad = JsonDocument.Parse("{\"name\":\"Secret Project With No Id\"}").RootElement.Clone();
        _workstream.GetProjectsPageAsync(1, 3, CorrelationId, Arg.Any<CancellationToken>()).Returns(new WorkstreamProjectsPage([Item(1), bad], 1, 3));

        var exception = await Assert.ThrowsAsync<ExternalApiResponseException>(() => _service.FetchProjectsAsync(CorrelationId, CancellationToken.None));

        Assert.Contains("page 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no id", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret Project", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureCheckingTheToken_PropagatesAsAnError_NotAsAnInvalidToken()
    {
        _workstream.IsTokenValidAsync(CorrelationId, Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new ExternalApiUnavailableException("Workstream", "down")));

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => _service.FetchProjectsAsync(CorrelationId, CancellationToken.None));
    }

    [Fact]
    public async Task TheCancellationTokenReachesEveryCall()
    {
        GivenPages(Page(1, 1, 2, 3), Page(2, 4));
        using var cancellation = new CancellationTokenSource();

        await _service.FetchProjectsAsync(CorrelationId, cancellation.Token);

        await _workstream.Received(1).IsTokenValidAsync(CorrelationId, cancellation.Token);
        await _workstream.Received(2).GetProjectsPageAsync(Arg.Any<int>(), 3, CorrelationId, cancellation.Token);
    }

    private static Exception Failure(Type type) => type.Name switch
    {
        nameof(ExternalApiUnavailableException) => new ExternalApiUnavailableException("Workstream", "down", 503),
        nameof(ExternalAuthenticationException) => new ExternalAuthenticationException("Workstream", "rejected", 401),
        nameof(ExternalApiResponseException) => new ExternalApiResponseException("Workstream", "not a list"),
        _ => new ExternalApiRejectedException("Workstream", "bad", 400)
    };
}
