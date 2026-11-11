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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public class RequestWorkerTests
{
    private const string TimeFrameJson = "{\"correlation_id\":\"c1\",\"event_type\":\"engineer-stats\",\"emails\":[\"jane@arbisoft.com\"],\"time_frame\":\"24h\",\"requested_on\":\"2026-11-11T12:00:00+00:00\"}";

    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IEmailMappingRepository _mappings = Substitute.For<IEmailMappingRepository>();
    private readonly ICallbackAvailability _callbacks = Substitute.For<ICallbackAvailability>();
    private readonly IEngineerStatsCollector _collector = Substitute.For<IEngineerStatsCollector>();
    private readonly ICallbackDelivery _delivery = Substitute.For<ICallbackDelivery>();
    private readonly IRequestAttemptLog _attempts = Substitute.For<IRequestAttemptLog>();
    private readonly RequestWorker _worker;

    public RequestWorkerTests()
    {
        GivenLog(RequestStatus.Queued);
        _callbacks.IsAvailableAsync(7, "engineer-stats", Arg.Any<CancellationToken>()).Returns(true);
        _mappings.GetMappedEmailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _collector.CollectAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Stats(call.ArgAt<string>(0))));
        _delivery.DeliverAsync(Arg.Any<RequestLog>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(WorkerOutcome.Completed);
        _worker = new RequestWorker(_logs, _mappings, _callbacks, _collector, _delivery, _attempts, NullLogger<RequestWorker>.Instance);
    }

    private static EmployeeStatsDto Stats(string email)
    {
        var data = new Dictionary<string, JsonElement> { [email] = JsonDocument.Parse("{\"n\":1}").RootElement.Clone() };
        return new EmployeeStatsDto(email, ["alt@gmail.com"], data, data, data, data);
    }

    private static QueueDelivery Delivery(string json = TimeFrameJson) => new(new QueueMessage("c1", "engineer-stats", json), 5, false, 0);

    private void GivenLog(string status, string eventType = "engineer-stats")
    {
        _logs.GetByCorrelationIdAsync("c1", Arg.Any<CancellationToken>()).Returns(new RequestLog { Id = 41, ClientRefId = 7, CorrelationId = "c1", Status = status, EventType = eventType });
    }

    [Fact]
    public async Task ANormalRequest_GoesToProcessing_CollectsStats_AndHandsThePayloadToDelivery()
    {
        var outcome = await _worker.ProcessAsync(Delivery(), CancellationToken.None);

        Assert.Equal(WorkerOutcome.Completed, outcome);
        Received.InOrder(() =>
        {
            _logs.UpdateStatusAsync(41, RequestStatus.Processing, Arg.Any<CancellationToken>());
            _collector.CollectAsync("jane@arbisoft.com", Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), "c1", Arg.Any<CancellationToken>());
            _delivery.DeliverAsync(Arg.Any<RequestLog>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ThePayload_HasTheAgreedShape_ForATimeFrame()
    {
        string? payload = null;
        await _delivery.DeliverAsync(Arg.Any<RequestLog>(), Arg.Do<string>(p => payload = p), Arg.Any<CancellationToken>());

        await _worker.ProcessAsync(Delivery(), CancellationToken.None);

        var root = JsonDocument.Parse(payload!).RootElement;
        Assert.Equal("c1", root.GetProperty("correlation_id").GetString());
        Assert.Equal("24h", root.GetProperty("time_frame").GetString());
        Assert.False(root.TryGetProperty("from", out _));
        Assert.False(root.TryGetProperty("to", out _));
        var engineer = Assert.Single(root.GetProperty("engineers").EnumerateArray());
        Assert.Equal("jane@arbisoft.com", engineer.GetProperty("email").GetString());
        Assert.Equal("alt@gmail.com", Assert.Single(engineer.GetProperty("mapped_emails").EnumerateArray()).GetString());
        foreach (var source in new[] { "jira", "github", "ai_usage", "ai_cost" })
        {
            Assert.Equal(1, engineer.GetProperty(source).GetProperty("jane@arbisoft.com").GetProperty("n").GetInt32());
        }
    }

    [Fact]
    public async Task ThePayload_ForARangeBatch_HasFromAndToAndOneEntryPerEngineer()
    {
        const string json = "{\"correlation_id\":\"c1\",\"event_type\":\"engineer-stats\",\"emails\":[\"a@arbisoft.com\",\"b@arbisoft.com\"],\"requested_on\":\"2026-11-11T12:00:00+00:00\",\"from\":\"2026-11-01T00:00:00Z\",\"to\":\"2026-11-08T00:00:00Z\"}";
        string? payload = null;
        await _delivery.DeliverAsync(Arg.Any<RequestLog>(), Arg.Do<string>(p => payload = p), Arg.Any<CancellationToken>());

        await _worker.ProcessAsync(Delivery(json), CancellationToken.None);

        var root = JsonDocument.Parse(payload!).RootElement;
        Assert.Equal("2026-11-01T00:00:00Z", root.GetProperty("from").GetString());
        Assert.Equal("2026-11-08T00:00:00Z", root.GetProperty("to").GetString());
        Assert.False(root.TryGetProperty("time_frame", out _));
        Assert.Equal(["a@arbisoft.com", "b@arbisoft.com"], root.GetProperty("engineers").EnumerateArray().Select(e => e.GetProperty("email").GetString()));
        await _collector.Received(1).CollectAsync("a@arbisoft.com", Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), "c1", Arg.Any<CancellationToken>());
        await _collector.Received(1).CollectAsync("b@arbisoft.com", Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), "c1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachEngineersMappedEmails_AreLoadedOneAtATime_BeforeAnythingRunsInParallel()
    {
        const string json = "{\"correlation_id\":\"c1\",\"event_type\":\"engineer-stats\",\"emails\":[\"a@arbisoft.com\",\"b@arbisoft.com\",\"c@arbisoft.com\"],\"time_frame\":\"24h\",\"requested_on\":\"2026-11-11T12:00:00+00:00\"}";
        var inFlight = 0;
        var peak = 0;
        var lookupsFinishedBeforeCollection = true;
        var lookups = 0;
        _mappings.GetMappedEmailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            peak = Math.Max(peak, now);
            await Task.Delay(30);
            Interlocked.Decrement(ref inFlight);
            Interlocked.Increment(ref lookups);
            return (IReadOnlyList<string>)["alt@gmail.com"];
        });
        _collector.CollectAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lookupsFinishedBeforeCollection &= Volatile.Read(ref lookups) == 3;
                return Task.FromResult(Stats(call.ArgAt<string>(0)));
            });

        await _worker.ProcessAsync(Delivery(json), CancellationToken.None);

        Assert.Equal(1, peak);
        Assert.True(lookupsFinishedBeforeCollection);
        await _collector.Received(3).CollectAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(mapped => mapped.SequenceEqual(new[] { "alt@gmail.com" })), Arg.Any<StatsQuery>(), "c1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMessageWhoseLogRowIsNotThereYet_IsPutBack_NotLost()
    {
        _logs.GetByCorrelationIdAsync("c1", Arg.Any<CancellationToken>()).Returns((RequestLog?)null);

        Assert.Equal(WorkerOutcome.Requeue, await _worker.ProcessAsync(Delivery(), CancellationToken.None));

        await _collector.DidNotReceiveWithAnyArgs().CollectAsync(default!, default!, default!, default!, default);
    }

    [Theory]
    [InlineData(RequestStatus.Success)]
    [InlineData(RequestStatus.Failed)]
    [InlineData(RequestStatus.RevokedNoCallbackUrl)]
    public async Task ARepeatDeliveryOfAFinishedRequest_IsAcknowledged_WithoutDoingTheWorkAgain(string status)
    {
        GivenLog(status);

        Assert.Equal(WorkerOutcome.Completed, await _worker.ProcessAsync(Delivery(), CancellationToken.None));

        await _collector.DidNotReceiveWithAnyArgs().CollectAsync(default!, default!, default!, default!, default);
        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default!, default);
        await _logs.DidNotReceiveWithAnyArgs().UpdateStatusAsync(default, default!, default);
    }

    [Theory]
    [InlineData(RequestStatus.Received)]
    [InlineData(RequestStatus.Queued)]
    [InlineData(RequestStatus.Processing)]
    [InlineData(RequestStatus.Processed)]
    public async Task AnUnfinishedRequest_IsProcessed(string status)
    {
        GivenLog(status);

        await _worker.ProcessAsync(Delivery(), CancellationToken.None);

        await _delivery.ReceivedWithAnyArgs(1).DeliverAsync(default!, default!, default);
    }

    [Fact]
    public async Task AnUnreadableBody_IsDeadLettered_WithTheReasonRecorded_AndTheRequestFailed()
    {
        var outcome = await _worker.ProcessAsync(Delivery("{ not json"), CancellationToken.None);

        Assert.Equal(WorkerOutcome.DeadLetter, outcome);
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.FetchFailed, "{}", Arg.Is<string?>(reason => reason != null && reason.Contains("not valid JSON")), Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
        await _collector.DidNotReceiveWithAnyArgs().CollectAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ACallbackRemovedWhileQueued_RevokesTheRequest_BeforeDevLakeIsCalled()
    {
        _callbacks.IsAvailableAsync(7, "engineer-stats", Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal(WorkerOutcome.Completed, await _worker.ProcessAsync(Delivery(), CancellationToken.None));

        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.RevokedNoCallbackUrl, Arg.Any<CancellationToken>());
        await _logs.DidNotReceive().UpdateStatusAsync(41, RequestStatus.Processing, Arg.Any<CancellationToken>());
        await _collector.DidNotReceiveWithAnyArgs().CollectAsync(default!, default!, default!, default!, default);
    }

    [Theory]
    [InlineData(typeof(ExternalApiRejectedException))]
    [InlineData(typeof(ExternalApiResponseException))]
    public async Task WhenDevLakeRefusesTheRequest_ItFails_OnItsOwn_WithTheReasonRecorded(Type exceptionType)
    {
        _collector.CollectAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmployeeStatsDto>(CreateFailure(exceptionType)));

        var outcome = await _worker.ProcessAsync(Delivery(), CancellationToken.None);

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.FetchFailed, "{}", Arg.Is<string?>(reason => reason != null && reason.Contains("DevLake")), Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(typeof(ExternalAuthenticationException))]
    [InlineData(typeof(ExternalApiUnavailableException))]
    public async Task WhenDevLakeCannotBeUsedRightNow_TheRequestGoesBackToQueued_AndTheRunStops(Type exceptionType)
    {
        _collector.CollectAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmployeeStatsDto>(CreateFailure(exceptionType)));

        var outcome = await _worker.ProcessAsync(Delivery(), CancellationToken.None);

        Assert.Equal(WorkerOutcome.StopRun, outcome);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Queued, Arg.Any<CancellationToken>());
        await _logs.DidNotReceive().UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
        await _attempts.DidNotReceiveWithAnyArgs().RecordAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task AShutdownWhileCollecting_PutsTheRequestBackToQueued()
    {
        using var stopping = new CancellationTokenSource();
        _collector.CollectAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<StatsQuery>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await stopping.CancelAsync();
                call.ArgAt<CancellationToken>(4).ThrowIfCancellationRequested();
                return Stats("x");
            });

        var outcome = await _worker.ProcessAsync(Delivery(), stopping.Token);

        Assert.Equal(WorkerOutcome.Requeue, outcome);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Queued, Arg.Any<CancellationToken>());
        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(WorkerOutcome.Completed)]
    [InlineData(WorkerOutcome.Requeue)]
    [InlineData(WorkerOutcome.StopRun)]
    public async Task WhateverDeliveryDecides_IsWhatTheWorkerReports(WorkerOutcome decided)
    {
        _delivery.DeliverAsync(Arg.Any<RequestLog>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(decided);

        Assert.Equal(decided, await _worker.ProcessAsync(Delivery(), CancellationToken.None));
    }

    private static Exception CreateFailure(Type type) => type.Name switch
    {
        nameof(ExternalApiRejectedException) => new ExternalApiRejectedException("DevLake", "DevLake rejected the request (404).", 404),
        nameof(ExternalApiResponseException) => new ExternalApiResponseException("DevLake", "DevLake answered with something that is not JSON."),
        nameof(ExternalAuthenticationException) => new ExternalAuthenticationException("DevLake", "DevLake rejected our credentials (401).", 401),
        _ => new ExternalApiUnavailableException("DevLake", "DevLake was unavailable (503).", 503)
    };
}
