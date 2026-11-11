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
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public class CallbackDeliveryTests
{
    private const string Payload = "{\"correlation_id\":\"c1\"}";

    private static readonly RequestLog Request = new() { Id = 41, ClientRefId = 7, CorrelationId = "c1", EventType = "engineer-stats", Status = RequestStatus.Processing };

    private readonly ISophiaClient _sophia = Substitute.For<ISophiaClient>();
    private readonly IClientConfigRepository _configs = Substitute.For<IClientConfigRepository>();
    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IRequestAttemptLog _attempts = Substitute.For<IRequestAttemptLog>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly CallbackDelivery _delivery;

    public CallbackDeliveryTests()
    {
        GivenConfig(maxAttempts: 3);
        _attempts.CountDeliveryAttemptsAsync(41, Arg.Any<CancellationToken>()).Returns(0);
        var options = Options.Create(new ProcessingOptions { DeliveryRetryDelaySeconds = 30 });
        _delivery = new CallbackDelivery(_sophia, _configs, _logs, _attempts, options, _time, NullLogger<CallbackDelivery>.Instance);
    }

    private void GivenConfig(int maxAttempts, bool active = true)
    {
        _configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(new ClientConfig { Id = 3, ClientRefId = 7, MaxAttempts = maxAttempts, IsActive = active });
    }

    private static SophiaDeliveryResult Ok() => new(true, 200, "{\"ok\":true}");

    private static SophiaDeliveryResult Failed(int status = 500) => new(false, status, "{\"error\":\"x\"}");

    private void SophiaAnswers(params object[] answers)
    {
        var queue = new Queue<object>(answers);
        _sophia.PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return next is Exception exception ? Task.FromException<SophiaDeliveryResult>(exception) : Task.FromResult((SophiaDeliveryResult)next);
        });
    }

    /// <summary>Runs a delivery, moving the clock past each wait, and returns how long it waited in total.</summary>
    private async Task<(WorkerOutcome Outcome, TimeSpan Waited)> RunAsync(CancellationToken stopping = default)
    {
        var started = _time.GetUtcNow();
        var delivery = _delivery.DeliverAsync(Request, Payload, stopping);
        while (!delivery.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, CancellationToken.None);
        }

        return (await delivery, _time.GetUtcNow() - started);
    }

    [Fact]
    public async Task ADeliveryThatWorksFirstTime_RecordsOneAttempt_AndMarksTheRequestSuccess()
    {
        SophiaAnswers(Ok());

        var (outcome, waited) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        Assert.True(waited < TimeSpan.FromSeconds(5));
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.Delivered, "{\"ok\":true}", null, Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Success, Arg.Any<CancellationToken>());
        await _sophia.Received(1).PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedDelivery_IsRetriedAfterTheConfiguredWait_ThenSucceeds()
    {
        SophiaAnswers(Failed(500), Ok());

        var (outcome, waited) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        Assert.InRange(waited, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.DeliveryFailed, "{\"error\":\"x\"}", "HTTP 500", Arg.Any<CancellationToken>());
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.Delivered, Arg.Any<string>(), Arg.Is<string?>(failure => failure == null), Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Success, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheWaitDoublesAfterEachFailure()
    {
        SophiaAnswers(Failed(), Failed(), Ok());

        var (_, waited) = await RunAsync();

        Assert.InRange(waited, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(100));
    }

    [Fact]
    public async Task WhenEveryAttemptFails_TheRequestIsMarkedFailed_AfterExactlyMaxAttemptsTries()
    {
        SophiaAnswers(Failed(502));

        var (outcome, _) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _sophia.Received(3).PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Any<CancellationToken>());
        await _attempts.Received(3).RecordAsync(41, DeliveryAttemptStatus.DeliveryFailed, Arg.Any<string>(), "HTTP 502", Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
        await _logs.DidNotReceive().UpdateStatusAsync(41, RequestStatus.Success, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheNumberOfAttempts_ComesFromTheClientsConfig()
    {
        GivenConfig(maxAttempts: 1);
        SophiaAnswers(Failed());

        var (_, waited) = await RunAsync();

        await _sophia.Received(1).PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Any<CancellationToken>());
        Assert.True(waited < TimeSpan.FromSeconds(5));
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterARestart_AttemptsAlreadyMadeCountAgainstTheLimit()
    {
        _attempts.CountDeliveryAttemptsAsync(41, Arg.Any<CancellationToken>()).Returns(2);
        SophiaAnswers(Failed());

        await RunAsync();

        await _sophia.Received(1).PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTheLimitWasAlreadyReached_NothingMoreIsSent_AndTheRequestIsFailed()
    {
        _attempts.CountDeliveryAttemptsAsync(41, Arg.Any<CancellationToken>()).Returns(3);

        var (outcome, _) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _sophia.DidNotReceiveWithAnyArgs().PostCallbackAsync(default, default!, default!, default!, default);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnreachableSophia_CountsAsAFailedAttempt_WithTheReason()
    {
        SophiaAnswers(new ExternalApiUnavailableException("Sophia", "Sophia could not be reached or timed out."), Ok());

        var (outcome, _) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.DeliveryFailed, "{}", "Sophia could not be reached or timed out.", Arg.Any<CancellationToken>());
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Success, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACredentialsProblem_PutsTheRequestBackAndStopsTheRun_WithoutSpendingAnAttempt()
    {
        SophiaAnswers(new ExternalAuthenticationException("Sophia", "rejected", 401));

        var (outcome, _) = await RunAsync();

        Assert.Equal(WorkerOutcome.StopRun, outcome);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Queued, Arg.Any<CancellationToken>());
        await _attempts.DidNotReceiveWithAnyArgs().RecordAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task ACallbackRemovedWhileDelivering_RevokesTheRequest()
    {
        SophiaAnswers(new CallbackNotConfiguredException("gone"));

        var (outcome, _) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.RevokedNoCallbackUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInactiveConfig_RevokesTheRequest_WithoutSending()
    {
        GivenConfig(maxAttempts: 3, active: false);

        var (outcome, _) = await RunAsync();

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _sophia.DidNotReceiveWithAnyArgs().PostCallbackAsync(default, default!, default!, default!, default);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.RevokedNoCallbackUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShutdownDuringTheWait_PutsTheRequestBack_ForAnotherRun()
    {
        SophiaAnswers(Failed());
        using var stopping = new CancellationTokenSource();
        var delivery = _delivery.DeliverAsync(Request, Payload, stopping.Token);
        await Task.Delay(50);

        await stopping.CancelAsync();
        var outcome = await delivery;

        Assert.Equal(WorkerOutcome.Requeue, outcome);
        await _logs.Received(1).UpdateStatusAsync(41, RequestStatus.Queued, Arg.Any<CancellationToken>());
        await _sophia.Received(1).PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APostIsNeverCutShortByAShutdown_SoADeliveredResultIsAlwaysRecorded()
    {
        SophiaAnswers(Ok());
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var (outcome, _) = await RunAsync(stopping.Token);

        Assert.Equal(WorkerOutcome.Completed, outcome);
        await _sophia.Received(1).PostCallbackAsync(7, "engineer-stats", Payload, "c1", Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
        await _attempts.Received(1).RecordAsync(41, DeliveryAttemptStatus.Delivered, Arg.Any<string>(), Arg.Is<string?>(failure => failure == null), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }
}
