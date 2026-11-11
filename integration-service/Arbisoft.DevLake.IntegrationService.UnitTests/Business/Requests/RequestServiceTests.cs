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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IClientConfigRepository _configs = Substitute.For<IClientConfigRepository>();
    private readonly IRequestQueuePublisher _publisher = Substitute.For<IRequestQueuePublisher>();
    private readonly IDbSession _session = Substitute.For<IDbSession>();
    private readonly RequestService _service;

    public RequestServiceTests()
    {
        _logs.TryInsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>()).Returns(41L);
        _logs.InsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>()).Returns(42L);
        _service = new RequestService(_logs, _configs, _publisher, _session, new FakeTimeProvider(Now), NullLogger<RequestService>.Instance);
    }

    private static RequestSubmission Submission(string eventType = RequestEventTypes.EngineerStats, params string[] emails)
    {
        var timeFrame = StatsPeriod.TryParse("24h", null, null, out var parsed, out _) ? parsed! : throw new InvalidOperationException("Test time frame is invalid.");
        return new RequestSubmission(7, eventType, "/api/v1/engineers/{email}/stats?tf={timeFrame}", emails.Length == 0 ? ["jane@arbisoft.com"] : emails, timeFrame, "the-key");
    }

    private void GivenCallback(string eventType = RequestEventTypes.EngineerStats, bool configActive = true, bool urlExists = true)
    {
        _configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(new ClientConfig { Id = 3, ClientRefId = 7, IsActive = configActive });
        _configs.GetActiveOutboundUrlAsync(3, eventType, Arg.Any<CancellationToken>()).Returns(urlExists ? new ClientConfigOutboundUrl { Id = 9 } : null);
    }

    [Fact]
    public async Task Submit_WithACallback_LogsThenPublishesThenMarksQueuedThenCommits()
    {
        GivenCallback();

        var result = await _service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Queued, result.Outcome);
        Received.InOrder(() =>
        {
            _session.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _logs.TryInsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>());
            _logs.InsertJsonAsync(Arg.Any<RequestLogJson>(), Arg.Any<CancellationToken>());
            _publisher.PublishAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>());
            _logs.UpdateStatusAsync(41, RequestStatus.Queued, Arg.Any<CancellationToken>());
            _session.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Submit_LogsTheRequestWithItsKeyAndAnUrlThatHasNoEmail()
    {
        GivenCallback();

        var result = await _service.SubmitAsync(Submission(), CancellationToken.None);

        await _logs.Received(1).TryInsertAsync(
            Arg.Is<RequestLog>(log => log.ClientRefId == 7 && log.CorrelationId == result.CorrelationId && log.Status == RequestStatus.Received
                && log.IdempotencyKey == "the-key" && log.EventType == "engineer-stats" && log.CreatedOn == Now && !log.Url.Contains('@', StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_QueuesAndStoresTheSameJsonBody_WithTheCorrelationId()
    {
        GivenCallback(RequestEventTypes.EngineerStatsBatch);
        QueueMessage? published = null;
        RequestLogJson? stored = null;
        await _publisher.PublishAsync(Arg.Do<QueueMessage>(m => published = m), Arg.Any<CancellationToken>());
        await _logs.InsertJsonAsync(Arg.Do<RequestLogJson>(j => stored = j), Arg.Any<CancellationToken>());

        var result = await _service.SubmitAsync(Submission(RequestEventTypes.EngineerStatsBatch, "a@arbisoft.com", "b@arbisoft.com"), CancellationToken.None);

        Assert.Equal(result.CorrelationId, published!.CorrelationId);
        Assert.Equal("engineer-stats-batch", published.EventType);
        Assert.Equal(published.Json, stored!.Json);
        Assert.Equal(41, stored.RequestLogId);
        var body = JsonDocument.Parse(published.Json).RootElement;
        Assert.Equal(result.CorrelationId, body.GetProperty("correlation_id").GetString());
        Assert.Equal("24h", body.GetProperty("time_frame").GetString());
        Assert.Equal(["a@arbisoft.com", "b@arbisoft.com"], body.GetProperty("emails").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Submit_ForARange_QueuesFromAndToInsteadOfATimeFrame()
    {
        GivenCallback();
        QueueMessage? published = null;
        await _publisher.PublishAsync(Arg.Do<QueueMessage>(m => published = m), Arg.Any<CancellationToken>());
        var period = StatsPeriod.TryParse(null, "2026-11-01T05:00:00+05:00", "2026-11-08T00:00:00Z", out var parsed, out _) ? parsed! : throw new InvalidOperationException();
        var submission = Submission() with { Period = period, RequestUrl = "/api/v1/engineers/{email}/stats?from={from}&to={to}" };

        await _service.SubmitAsync(submission, CancellationToken.None);

        var body = JsonDocument.Parse(published!.Json).RootElement;
        Assert.Equal("2026-11-01T00:00:00Z", body.GetProperty("from").GetString());
        Assert.Equal("2026-11-08T00:00:00Z", body.GetProperty("to").GetString());
        Assert.False(body.TryGetProperty("time_frame", out _));
    }

    [Fact]
    public async Task Submit_ForATimeFrame_LeavesFromAndToOut()
    {
        GivenCallback();
        QueueMessage? published = null;
        await _publisher.PublishAsync(Arg.Do<QueueMessage>(m => published = m), Arg.Any<CancellationToken>());

        await _service.SubmitAsync(Submission(), CancellationToken.None);

        var body = JsonDocument.Parse(published!.Json).RootElement;
        Assert.Equal("24h", body.GetProperty("time_frame").GetString());
        Assert.False(body.TryGetProperty("from", out _));
        Assert.False(body.TryGetProperty("to", out _));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Submit_WithoutAnActiveCallbackUrl_LogsItRevokedAndDoesNotQueue(bool configActive, bool urlExists)
    {
        GivenCallback(configActive: configActive, urlExists: urlExists);

        var result = await _service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.Equal(SubmissionOutcome.NoCallbackUrl, result.Outcome);
        await _logs.Received(1).InsertAsync(
            Arg.Is<RequestLog>(log => log.Status == RequestStatus.RevokedNoCallbackUrl && log.IdempotencyKey == null && log.CorrelationId == result.CorrelationId),
            Arg.Any<CancellationToken>());
        await _logs.Received(1).InsertJsonAsync(Arg.Is<RequestLogJson>(j => j.RequestLogId == 42), Arg.Any<CancellationToken>());
        await _session.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _logs.DidNotReceiveWithAnyArgs().TryInsertAsync(default!, default);
    }

    [Fact]
    public async Task Submit_ForAClientWithNoConfigAtAll_IsRevoked()
    {
        _configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns((ClientConfig?)null);

        Assert.Equal(SubmissionOutcome.NoCallbackUrl, (await _service.SubmitAsync(Submission(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Submit_WhenAnotherRequestWonTheKey_RollsBackBeforePublishing_AndReturnsTheWinner()
    {
        GivenCallback();
        _logs.TryInsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        _logs.GetByIdempotencyKeyAsync("the-key", Arg.Any<CancellationToken>()).Returns(new RequestLog { Id = 1, CorrelationId = "winner" });

        var result = await _service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Duplicate, result.Outcome);
        Assert.Equal("winner", result.CorrelationId);
        await _session.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _session.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Submit_WhenTheWinnerVanishedBeforeItWasRead_TriesAgain()
    {
        GivenCallback();
        _logs.TryInsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>()).Returns((long?)null, 41L);
        _logs.GetByIdempotencyKeyAsync("the-key", Arg.Any<CancellationToken>()).Returns((RequestLog?)null);

        var result = await _service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Queued, result.Outcome);
        await _session.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_WhenTheKeyKeepsChangingHands_GivesUpAfterThreeTries()
    {
        GivenCallback();
        _logs.TryInsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        _logs.GetByIdempotencyKeyAsync("the-key", Arg.Any<CancellationToken>()).Returns((RequestLog?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync(Submission(), CancellationToken.None));

        await _logs.Received(3).TryInsertAsync(Arg.Any<RequestLog>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(typeof(QueueFullException))]
    [InlineData(typeof(QueueUnavailableException))]
    public async Task Submit_WhenTheQueueRejectsTheMessage_NeverCommits_SoNothingIsLeftBehind(Type exceptionType)
    {
        GivenCallback();
        _publisher.PublishAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException((Exception)Activator.CreateInstance(exceptionType, "rejected", null)!));

        await Assert.ThrowsAsync(exceptionType, () => _service.SubmitAsync(Submission(), CancellationToken.None));

        await _session.DidNotReceiveWithAnyArgs().CommitAsync(default);
        await _logs.DidNotReceiveWithAnyArgs().UpdateStatusAsync(default, default!, default);
    }
}
