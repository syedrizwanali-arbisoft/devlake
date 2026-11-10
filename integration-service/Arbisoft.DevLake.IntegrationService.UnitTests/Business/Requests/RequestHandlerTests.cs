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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestHandlerTests
{
    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IIdempotencyCheck _idempotency = Substitute.For<IIdempotencyCheck>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly RequestHandler _handler;
    private StatsSubmission? _submitted;

    public RequestHandlerTests()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Build());
        _idempotency.ComputeHash(default!, default!, default!).ReturnsForAnyArgs("hash");
        _idempotency.FindDuplicateAsync(default, default!).ReturnsForAnyArgs((string?)null);
        _requestService.QueueAsync(Arg.Do<StatsSubmission>(s => _submitted = s), Arg.Any<CancellationToken>())
            .Returns(new StatsSubmissionResult(StatsSubmissionOutcome.Accepted, "new-id", ""));
        _handler = new RequestHandler(_clients, _idempotency, _requestService, Options.Create(new RequestOptions()));
    }

    [Fact]
    public async Task SubmitSingle_NormalizesAndQueuesAValidRequest()
    {
        var result = await _handler.SubmitSingleAsync("sophia", " ABC@Arbisoft.com ", "24h", "/url");

        Assert.Equal(StatsSubmissionOutcome.Accepted, result.Outcome);
        Assert.Equal("new-id", result.CorrelationId);
        Assert.NotNull(_submitted);
        Assert.Equal(1, _submitted.ClientRefId);
        Assert.Equal("sophia", _submitted.ClientId);
        Assert.Equal(RequestEventTypes.EngineerStats, _submitted.EventType);
        Assert.Equal(["abc@arbisoft.com"], _submitted.Emails);
        Assert.Equal("24h", _submitted.TimeFrame.ToString());
        Assert.Equal("hash", _submitted.RequestHash);
        Assert.Equal("/url", _submitted.Url);
    }

    [Fact]
    public async Task SubmitBatch_DropsRepeatedEmails()
    {
        await _handler.SubmitBatchAsync("sophia", ["a@arbisoft.com", "A@arbisoft.com", "b@arbisoft.com"], "1w", "/url");

        Assert.Equal(RequestEventTypes.EngineerStatsBatch, _submitted!.EventType);
        Assert.Equal(["a@arbisoft.com", "b@arbisoft.com"], _submitted.Emails);
    }

    [Fact]
    public async Task Submit_ReturnsTheOriginalCorrelationIdForADuplicate()
    {
        _idempotency.FindDuplicateAsync(1, "hash", Arg.Any<CancellationToken>()).Returns("original-id");

        var result = await _handler.SubmitSingleAsync("sophia", "abc@arbisoft.com", "24h", "/url");

        Assert.Equal(new StatsSubmissionResult(StatsSubmissionOutcome.Duplicate, "original-id", ""), result);
        await _requestService.DidNotReceiveWithAnyArgs().QueueAsync(default!, default);
    }

    [Theory]
    [InlineData("abc@gmail.com")]
    [InlineData("abc@sub.arbisoft.com")]
    [InlineData("abc@arbisoft.com.evil.com")]
    [InlineData("not-an-email")]
    [InlineData("Abc <abc@arbisoft.com>")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SubmitSingle_RejectsEmailsThatAreNotArbisoftEmails(string? email)
    {
        var result = await _handler.SubmitSingleAsync("sophia", email, "24h", "/url");

        Assert.Equal(StatsSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains("arbisoft.com", result.Error);
        await _requestService.DidNotReceiveWithAnyArgs().QueueAsync(default!, default);
    }

    [Fact]
    public async Task SubmitBatch_NamesEveryInvalidEmail()
    {
        var result = await _handler.SubmitBatchAsync("sophia", ["ok@arbisoft.com", "x@gmail.com", "y"], "1w", "/url");

        Assert.Equal(StatsSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains("'x@gmail.com'", result.Error);
        Assert.Contains("'y'", result.Error);
        Assert.DoesNotContain("ok@", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("24H")]
    [InlineData("1m")]
    public async Task Submit_RejectsAnInvalidTimeFrame(string? timeFrame)
    {
        var result = await _handler.SubmitSingleAsync("sophia", "abc@arbisoft.com", timeFrame, "/url");

        Assert.Equal(StatsSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains("Time frame", result.Error);
    }

    [Fact]
    public async Task SubmitBatch_RejectsAnEmptyOrMissingList()
    {
        Assert.Equal(StatsSubmissionOutcome.Invalid, (await _handler.SubmitBatchAsync("sophia", [], "1w", "/url")).Outcome);
        Assert.Equal(StatsSubmissionOutcome.Invalid, (await _handler.SubmitBatchAsync("sophia", null, "1w", "/url")).Outcome);
    }

    [Fact]
    public async Task SubmitBatch_AllowsTwentyEmailsButNotTwentyOne()
    {
        var twenty = Enumerable.Range(1, 20).Select(i => $"e{i}@arbisoft.com").ToList();

        Assert.Equal(StatsSubmissionOutcome.Accepted, (await _handler.SubmitBatchAsync("sophia", twenty, "1w", "/url")).Outcome);
        var result = await _handler.SubmitBatchAsync("sophia", [.. twenty, "e21@arbisoft.com"], "1w", "/url");
        Assert.Equal(StatsSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains("at most 20", result.Error);
    }

    [Fact]
    public async Task Submit_RejectsAnInactiveClient()
    {
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new ClientBuilder().Inactive().Build());

        var result = await _handler.SubmitSingleAsync("sophia", "abc@arbisoft.com", "24h", "/url");

        Assert.Equal(StatsSubmissionOutcome.UnknownClient, result.Outcome);
    }
}
