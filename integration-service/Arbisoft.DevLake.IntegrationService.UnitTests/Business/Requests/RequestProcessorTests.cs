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

using System.Net;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestProcessorTests
{
    private const string Id = "3eef2f41-18a6-45e6-b620-e11aa9c69812";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IRequestQueueConsumer _consumer = Substitute.For<IRequestQueueConsumer>();
    private readonly IRequestLogRepository _logs = Substitute.For<IRequestLogRepository>();
    private readonly IEmailMappingRepository _mappings = Substitute.For<IEmailMappingRepository>();
    private readonly ICallbackTargetResolver _targets = Substitute.For<ICallbackTargetResolver>();
    private readonly IDevLakeClient _devLake = Substitute.For<IDevLakeClient>();
    private readonly ISophiaClient _sophia = Substitute.For<ISophiaClient>();
    private readonly ProcessingOptions _options = new() { MaxMessagesPerRun = 50, DeliveryRetryDelaySeconds = 0, MaxParallelDevLakeCalls = 4 };
    private readonly RequestLog _log = new() { Id = 42, CorrelationId = Id, Status = RequestLogStatus.Queued };
    private readonly CallbackTarget _target = new(
        new SophiaConnection(5, "https://sophia.test/", new StaticApiToken("t")), "POST", "/cb", MaxAttempts: 3);
    private readonly List<string> _statuses = [];
    private readonly List<RequestResponseAttempt> _attempts = [];
    private readonly RequestProcessor _processor;

    public RequestProcessorTests()
    {
        _logs.GetByCorrelationIdAsync(Id, Arg.Any<CancellationToken>()).Returns(_log);
        _logs.UpdateStatusAsync(42, Arg.Do<string>(s => _statuses.Add(s)), Arg.Any<CancellationToken>()).Returns(true);
        _logs.AddAttemptAsync(Arg.Do<RequestResponseAttempt>(a => _attempts.Add(a)), Arg.Any<CancellationToken>()).Returns(1L);
        _mappings.GetMappedEmailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _mappings.GetMappedEmailsAsync("abc@arbisoft.com", Arg.Any<CancellationToken>()).Returns(["abc@gmail.com", "ABC@arbisoft.com"]);
        _targets.ResolveAsync(1, RequestEventTypes.EngineerStats, Arg.Any<CancellationToken>()).Returns(_target);
        _targets.ResolveAsync(1, RequestEventTypes.EngineerStatsBatch, Arg.Any<CancellationToken>()).Returns(_target);
        _devLake.IsTokenValidAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
        _devLake.GetStatsAsync(default!, default, default!, default!).ReturnsForAnyArgs(call =>
            JsonSerializer.SerializeToElement(new { from = call.ArgAt<string>(0), category = call.ArgAt<DevLakeStatsCategory>(1).ToString() }));
        _sophia.GetAccessTokenAsync(Arg.Any<SophiaConnection>(), Arg.Any<CancellationToken>()).Returns("token");
        _sophia.SendCallbackAsync(default!, default!, default!, default!, default!)
            .ReturnsForAnyArgs(new CallbackResult(true, 200, "{\"ok\":true}", null));
        _processor = new RequestProcessor(_consumer, _logs, _mappings, _targets, _devLake, _sophia, Options.Create(_options),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero)), NullLogger<RequestProcessor>.Instance);
    }

    private static QueuedRequest Message(string eventType = RequestEventTypes.EngineerStats, params string[] emails) =>
        new(Id, JsonSerializer.Serialize(new StatsRequest(Id, 1, "sophia", eventType,
            emails.Length > 0 ? emails : ["abc@arbisoft.com"], "24h", DateTime.UtcNow), Web), null, false);

    private string? SentPayload() => _sophia.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ISophiaClient.SendCallbackAsync))
        .Select(c => (string)c.GetArguments()[3]!).LastOrDefault();

    [Fact]
    public async Task Process_FetchesEveryCategoryForEveryMappedEmailAndDelivers()
    {
        await _processor.ProcessAsync(Message());

        // The Arbisoft email, then its mappings, with the case-only duplicate removed.
        foreach (var email in new[] { "abc@arbisoft.com", "abc@gmail.com" })
        {
            foreach (var category in Enum.GetValues<DevLakeStatsCategory>())
            {
                await _devLake.Received(1).GetStatsAsync(email, category, "24h", Id, Arg.Any<CancellationToken>());
            }
        }

        Assert.Equal(8, _devLake.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IDevLakeClient.GetStatsAsync)));
        Assert.Equal([RequestLogStatus.Processing, RequestLogStatus.Processed, RequestLogStatus.Success], _statuses);
        await _sophia.Received(1).SendCallbackAsync(_target.Connection, "POST", "/cb", Arg.Any<string>(), Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Process_SendsTheMergedStatsPayload()
    {
        await _processor.ProcessAsync(Message());

        var payload = JsonDocument.Parse(SentPayload()!).RootElement;
        Assert.Equal(Id, payload.GetProperty("correlation_id").GetString());
        Assert.Equal("24h", payload.GetProperty("time_frame").GetString());
        var engineer = Assert.Single(payload.GetProperty("engineers").EnumerateArray());
        Assert.Equal("abc@arbisoft.com", engineer.GetProperty("email").GetString());
        Assert.Equal(["abc@arbisoft.com", "abc@gmail.com"], engineer.GetProperty("mapped_emails").EnumerateArray().Select(e => e.GetString()));
        var jira = engineer.GetProperty("jira").EnumerateArray().ToList();
        Assert.Equal(["abc@arbisoft.com", "abc@gmail.com"], jira.Select(j => j.GetProperty("email").GetString()));
        Assert.Equal("Jira", jira[0].GetProperty("stats").GetProperty("category").GetString());
        Assert.Equal("AiCost", engineer.GetProperty("ai_cost")[1].GetProperty("stats").GetProperty("category").GetString());
    }

    [Fact]
    public async Task Process_HandlesEveryEmailOfABatch()
    {
        await _processor.ProcessAsync(Message(RequestEventTypes.EngineerStatsBatch, "abc@arbisoft.com", "def@arbisoft.com"));

        var engineers = JsonDocument.Parse(SentPayload()!).RootElement.GetProperty("engineers").EnumerateArray().ToList();
        Assert.Equal(["abc@arbisoft.com", "def@arbisoft.com"], engineers.Select(e => e.GetProperty("email").GetString()));
        Assert.Equal(12, _devLake.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IDevLakeClient.GetStatsAsync)));
    }

    [Fact]
    public async Task Process_RecordsEachDeliveryAttemptAndRetriesUpToMaxAttempts()
    {
        _sophia.SendCallbackAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(
            new CallbackResult(false, 502, "bad gateway", "Callback returned 502."),
            new CallbackResult(false, null, "", "Callback failed: reset"),
            new CallbackResult(true, 200, "{\"ok\":true}", null));

        await _processor.ProcessAsync(Message());

        Assert.Equal(["Failed", "Failed", "Success"], _attempts.Select(a => a.AttemptStatus));
        Assert.Equal(["Callback returned 502.", "Callback failed: reset", null], _attempts.Select(a => a.FailureError));
        Assert.Equal("bad gateway", _attempts[0].ResponseJson);
        Assert.All(_attempts, a => Assert.Equal(42, a.RequestLogId));
        Assert.Equal(RequestLogStatus.Success, _statuses[^1]);
    }

    [Fact]
    public async Task Process_MarksTheRequestFailedAfterMaxAttempts()
    {
        _sophia.SendCallbackAsync(default!, default!, default!, default!, default!)
            .ReturnsForAnyArgs(new CallbackResult(false, 500, "", "Callback returned 500."));

        await _processor.ProcessAsync(Message());

        Assert.Equal(3, _attempts.Count);
        Assert.Equal(RequestLogStatus.Failed, _statuses[^1]);
    }

    [Fact]
    public async Task Process_RevokesARequestWhoseCallbackWasRemovedWhileQueued()
    {
        _targets.ResolveAsync(1, RequestEventTypes.EngineerStats, Arg.Any<CancellationToken>()).Returns((CallbackTarget?)null);

        await _processor.ProcessAsync(Message());

        Assert.Equal([RequestLogStatus.RevokedNoCallbackUrl], _statuses);
        await _devLake.DidNotReceiveWithAnyArgs().GetStatsAsync(default!, default, default!, default!);
    }

    [Theory]
    [InlineData(RequestLogStatus.Success)]
    [InlineData(RequestLogStatus.Failed)]
    [InlineData(RequestLogStatus.RevokedNoCallbackUrl)]
    public async Task Process_SkipsARequestThatAlreadyFinished(string status)
    {
        _log.Status = status;

        await _processor.ProcessAsync(Message());

        Assert.Empty(_statuses);
        await _sophia.DidNotReceiveWithAnyArgs().SendCallbackAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task Process_HaltsWithoutTouchingTheRequestWhenTheClientTokenFails()
    {
        _sophia.GetAccessTokenAsync(Arg.Any<SophiaConnection>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalApiAuthenticationException("Sophia", "token request returned 401."));

        await Assert.ThrowsAsync<QueueProcessingHaltedException>(() => _processor.ProcessAsync(Message()));

        Assert.Empty(_statuses);
        await _devLake.DidNotReceiveWithAnyArgs().GetStatsAsync(default!, default, default!, default!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Process_HaltsAndRequeuesWhenDevLakeIsDown(HttpStatusCode? status)
    {
        _devLake.GetStatsAsync(default!, default, default!, default!).ThrowsAsyncForAnyArgs(new ExternalApiException("DevLake", "down", status));

        await Assert.ThrowsAsync<QueueProcessingHaltedException>(() => _processor.ProcessAsync(Message()));

        Assert.Equal([RequestLogStatus.Processing, RequestLogStatus.Queued], _statuses);
    }

    [Fact]
    public async Task Process_FailsOnlyThisRequestForADevLakeClientError()
    {
        _devLake.GetStatsAsync(default!, default, default!, default!)
            .ThrowsAsyncForAnyArgs(new ExternalApiException("DevLake", "bad email", HttpStatusCode.BadRequest));

        await _processor.ProcessAsync(Message());

        Assert.Equal([RequestLogStatus.Processing, RequestLogStatus.Failed], _statuses);
        await _sophia.DidNotReceiveWithAnyArgs().SendCallbackAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task Process_HaltsWhenTheTokenIsLostDuringDelivery()
    {
        _sophia.SendCallbackAsync(default!, default!, default!, default!, default!)
            .ThrowsAsyncForAnyArgs(new ExternalApiAuthenticationException("Sophia", "refresh failed"));

        await Assert.ThrowsAsync<QueueProcessingHaltedException>(() => _processor.ProcessAsync(Message()));

        Assert.Equal(RequestLogStatus.Queued, _statuses[^1]);
    }

    [Fact]
    public async Task Process_FailsARequestWithAnIncompleteClientConfig()
    {
        _targets.ResolveAsync(1, RequestEventTypes.EngineerStats, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no tokenUrl"));

        await _processor.ProcessAsync(Message());

        Assert.Equal([RequestLogStatus.Failed], _statuses);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task Process_RejectsAMalformedMessage(string json)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.ProcessAsync(new QueuedRequest(Id, json, null, false)));
    }

    [Fact]
    public async Task Process_RejectsAMessageWithoutARequestLog()
    {
        _logs.GetByCorrelationIdAsync(Id, Arg.Any<CancellationToken>()).Returns((RequestLog?)null);

        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.ProcessAsync(Message()));
    }

    [Fact]
    public async Task Process_LimitsConcurrentDevLakeCalls()
    {
        var current = 0;
        var peak = 0;
        _devLake.GetStatsAsync(default!, default, default!, default!).ReturnsForAnyArgs(async _ =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref current);
            return JsonSerializer.SerializeToElement(new { });
        });
        _mappings.GetMappedEmailsAsync("abc@arbisoft.com", Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(1, 5).Select(i => $"m{i}@gmail.com").ToList());

        await _processor.ProcessAsync(Message());

        Assert.InRange(peak, 1, _options.MaxParallelDevLakeCalls);
    }

    [Fact]
    public async Task Run_SkipsTheQueueWhenTheDevLakeTokenIsInvalid()
    {
        _devLake.IsTokenValidAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _processor.RunAsync();

        Assert.True(result.Skipped);
        await _consumer.DidNotReceiveWithAnyArgs().ConsumeAsync(default!, default, default);
    }

    [Fact]
    public async Task Run_SkipsTheQueueWhenTheDevLakeCheckFails()
    {
        _devLake.IsTokenValidAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalApiAuthenticationException("DevLake", "DevLake:ApiToken is not set."));

        Assert.True((await _processor.RunAsync()).Skipped);
        await _consumer.DidNotReceiveWithAnyArgs().ConsumeAsync(default!, default, default);
    }

    [Fact]
    public async Task Run_ConsumesUpToTheConfiguredBatch()
    {
        _consumer.ConsumeAsync(default!, default, default).ReturnsForAnyArgs(new ConsumeResult(2, 0, 0));

        var result = await _processor.RunAsync();

        Assert.Equal(new ProcessingRunResult(false, new ConsumeResult(2, 0, 0)), result);
        await _consumer.Received(1).ConsumeAsync(Arg.Any<Func<QueuedRequest, CancellationToken, Task>>(), 50, Arg.Any<CancellationToken>());
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
