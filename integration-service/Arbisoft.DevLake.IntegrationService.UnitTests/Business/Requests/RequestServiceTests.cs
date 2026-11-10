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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly IClientConfigRepository _clientConfigs = Substitute.For<IClientConfigRepository>();
    private readonly IRequestLogRepository _requestLogs = Substitute.For<IRequestLogRepository>();
    private readonly IRequestQueuePublisher _publisher = Substitute.For<IRequestQueuePublisher>();
    private readonly RequestService _service;
    private readonly List<string> _events = [];
    private RequestLog? _log;
    private string? _loggedJson;

    public RequestServiceTests()
    {
        _clientConfigs.GetActiveOutboundUrlAsync(1, RequestEventTypes.EngineerStats, Arg.Any<CancellationToken>())
            .Returns(new ClientConfigOutboundUrl { Id = 3, IsActive = true });
        _requestLogs.AddAsync(Arg.Any<RequestLog>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _log = call.Arg<RequestLog>();
            _loggedJson = call.Arg<string>();
            _events.Add("log:" + _log.Status);
            return 42L;
        });
        _requestLogs.UpdateStatusAsync(42, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { _events.Add("status:" + call.ArgAt<string>(1)); return true; });
        _publisher.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { _events.Add("publish"); return Task.CompletedTask; });
        _service = new RequestService(_clientConfigs, _requestLogs, _publisher, new FakeTimeProvider(Now), NullLogger<RequestService>.Instance);
    }

    private static StatsSubmission Submission(string eventType = RequestEventTypes.EngineerStats) =>
        new(1, "sophia", eventType, ["abc@arbisoft.com"], TimeFrame.TryParse("24h", out var tf) ? tf : null!, "hash", "/url");

    [Fact]
    public async Task Queue_LogsReceivedThenPublishesThenMarksQueued()
    {
        var result = await _service.QueueAsync(Submission());

        Assert.Equal(StatsSubmissionOutcome.Accepted, result.Outcome);
        Assert.Equal(["log:Received", "publish", "status:Queued"], _events);
        Assert.Equal(result.CorrelationId, _log!.CorrelationId);
        Assert.Equal("hash", _log.RequestHash);
        Assert.Equal(RequestEventTypes.EngineerStats, _log.EventType);
        Assert.Equal("/url", _log.Url);
        Assert.Equal(Now, _log.CreatedOn);
    }

    [Fact]
    public async Task Queue_PublishesTheSameJsonItLogs()
    {
        string? published = null;
        _publisher.PublishAsync(Arg.Any<string>(), Arg.Do<string>(json => published = json), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _service.QueueAsync(Submission());

        Assert.Equal(_loggedJson, published);
        var request = JsonSerializer.Deserialize<StatsRequest>(published!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(new StatsRequest(result.CorrelationId, 1, "sophia", RequestEventTypes.EngineerStats, request.Emails, "24h", Now), request);
        Assert.Equal(["abc@arbisoft.com"], request.Emails);
    }

    [Fact]
    public async Task Queue_LogsARevokedRequestAndSkipsTheQueueWithoutACallback()
    {
        var result = await _service.QueueAsync(Submission(RequestEventTypes.EngineerStatsBatch));

        Assert.Equal(StatsSubmissionOutcome.NoCallbackUrl, result.Outcome);
        Assert.Equal("Revoked - no callback URL", result.Error);
        Assert.Equal(result.CorrelationId, _log!.CorrelationId);
        Assert.Equal(["log:Revoked - no callback URL"], _events);
    }

    [Fact]
    public async Task Queue_MarksTheLogFailedWhenTheQueueIsDown()
    {
        _publisher.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("no confirm"));

        var result = await _service.QueueAsync(Submission());

        Assert.Equal(StatsSubmissionOutcome.QueueUnavailable, result.Outcome);
        Assert.Equal(["log:Received", "status:Failed"], _events);
    }

    [Fact]
    public async Task Queue_KeepsTheCallersCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _publisher.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.QueueAsync(Submission(), cancelled.Token));
    }
}
