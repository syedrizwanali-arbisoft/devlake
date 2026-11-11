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
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public class EngineerStatsCollectorTests
{
    private static readonly StatsQuery Query = StatsQuery.ForTimeFrame("24h");

    private readonly IDevLakeClient _devLake = Substitute.For<IDevLakeClient>();
    private readonly EngineerStatsCollector _collector;

    public EngineerStatsCollectorTests()
    {
        _devLake.GetStatsAsync(Arg.Any<DevLakeStatsSource>(), Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Stats($"{call.ArgAt<string>(1)}|{call.ArgAt<DevLakeStatsSource>(0)}")));
        _collector = new EngineerStatsCollector(_devLake);
    }

    private static JsonElement Stats(string label) => JsonDocument.Parse($"{{\"label\":\"{label}\"}}").RootElement.Clone();

    private string[] _mapped = [];

    private void GivenMapped(params string[] emails) => _mapped = emails;

    private Task<EmployeeStatsDto> CollectAsync(string cid = "cid", StatsQuery? query = null, CancellationToken cancellationToken = default) =>
        _collector.CollectAsync("jane@arbisoft.com", _mapped, query ?? Query, cid, cancellationToken);

    [Fact]
    public async Task AnEngineerWithNoMappedEmails_IsCollectedForTheArbisoftEmailAlone_AcrossAllFourSources()
    {
        var result = await CollectAsync();

        Assert.Equal("jane@arbisoft.com", result.Email);
        Assert.Empty(result.MappedEmails);
        Assert.Equal("jane@arbisoft.com|Jira", result.Jira["jane@arbisoft.com"].GetProperty("label").GetString());
        Assert.Equal("jane@arbisoft.com|Github", result.Github["jane@arbisoft.com"].GetProperty("label").GetString());
        Assert.Equal("jane@arbisoft.com|AiUsage", result.AiUsage["jane@arbisoft.com"].GetProperty("label").GetString());
        Assert.Equal("jane@arbisoft.com|AiCost", result.AiCost["jane@arbisoft.com"].GetProperty("label").GetString());
        await _devLake.ReceivedWithAnyArgs(4).GetStatsAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task EveryMappedEmail_IsCollectedToo_OnEverySource()
    {
        GivenMapped("jane.d@gmail.com", "jdoe@users.noreply.github.com");

        var result = await CollectAsync();

        Assert.Equal(["jane.d@gmail.com", "jdoe@users.noreply.github.com"], result.MappedEmails);
        var expected = new[] { "jane@arbisoft.com", "jane.d@gmail.com", "jdoe@users.noreply.github.com" }.Order(StringComparer.Ordinal).ToList();
        foreach (var source in new[] { result.Jira, result.Github, result.AiUsage, result.AiCost })
        {
            Assert.Equal(expected, source.Keys.Order(StringComparer.Ordinal).ToList());
        }

        await _devLake.ReceivedWithAnyArgs(12).GetStatsAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task MappedEmails_AreCanonical_AndTheEngineersOwnEmailOrDuplicatesAreNotAskedForTwice()
    {
        GivenMapped("Jane.D@GMAIL.com", "jane.d@gmail.com", " JANE@arbisoft.com ");

        var result = await CollectAsync();

        Assert.Equal(["jane.d@gmail.com"], result.MappedEmails);
        await _devLake.ReceivedWithAnyArgs(8).GetStatsAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task TheQueryAndCorrelationId_AreSentOnEveryCall()
    {
        GivenMapped("jane.d@gmail.com");

        await CollectAsync("cid-9");

        await _devLake.ReceivedWithAnyArgs(8).GetStatsAsync(default, default!, default!, default!, default);
        await _devLake.DidNotReceive().GetStatsAsync(Arg.Any<DevLakeStatsSource>(), Arg.Any<string>(), Arg.Is<StatsQuery>(q => q != Query), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _devLake.DidNotReceive().GetStatsAsync(Arg.Any<DevLakeStatsSource>(), Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Is<string>(id => id != "cid-9"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllTheCallsStartTogether_NotOneAfterTheOther()
    {
        GivenMapped("jane.d@gmail.com");
        var gate = new TaskCompletionSource();
        var started = 0;
        _devLake.GetStatsAsync(Arg.Any<DevLakeStatsSource>(), Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                Interlocked.Increment(ref started);
                await gate.Task;
                return Stats("x");
            });

        var collecting = CollectAsync();
        await Task.Delay(100);

        Assert.Equal(8, Volatile.Read(ref started));
        gate.SetResult();
        await collecting;
    }

    [Fact]
    public async Task WhenAnyCallFails_TheWholeCollectionFails_SoNoResultIsDeliveredWithAGap()
    {
        _devLake.GetStatsAsync(DevLakeStatsSource.Github, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalApiRejectedException("DevLake", "nope", 404)));

        await Assert.ThrowsAsync<ExternalApiRejectedException>(() => CollectAsync());
    }

    [Fact]
    public async Task TheMostSeriousFailure_IsTheOneReported_NotTheFirst()
    {
        _devLake.GetStatsAsync(DevLakeStatsSource.Jira, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalApiRejectedException("DevLake", "bad", 400)));
        _devLake.GetStatsAsync(DevLakeStatsSource.Github, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalApiUnavailableException("DevLake", "down")));
        _devLake.GetStatsAsync(DevLakeStatsSource.AiCost, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalAuthenticationException("DevLake", "token", 401)));

        await Assert.ThrowsAsync<ExternalAuthenticationException>(() => CollectAsync());
    }

    [Fact]
    public async Task UnavailableOutranksRejected()
    {
        _devLake.GetStatsAsync(DevLakeStatsSource.Jira, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalApiRejectedException("DevLake", "bad", 400)));
        _devLake.GetStatsAsync(DevLakeStatsSource.AiUsage, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalApiUnavailableException("DevLake", "down")));

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => CollectAsync());
    }

    [Fact]
    public async Task EveryCallIsAwaited_EvenWhenOneFailsAtOnce_SoNoneIsLeftRunningUnobserved()
    {
        var finished = 0;
        _devLake.GetStatsAsync(DevLakeStatsSource.Jira, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JsonElement>(new ExternalApiRejectedException("DevLake", "bad", 400)));
        _devLake.GetStatsAsync(DevLakeStatsSource.Github, Arg.Any<string>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(150);
                Interlocked.Increment(ref finished);
                return Stats("slow");
            });

        await Assert.ThrowsAsync<ExternalApiRejectedException>(() => CollectAsync());

        Assert.Equal(1, Volatile.Read(ref finished));
    }
}
