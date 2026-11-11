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
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class RequestHandlerTests
{
    private static readonly string[] SingleEmail = ["jane.doe@arbisoft.com"];
    private static readonly string[] NormalizedBatch = ["b@arbisoft.com", "a@arbisoft.com"];

    private readonly IClientRepository _clients = Substitute.For<IClientRepository>();
    private readonly IIdempotencyCheck _idempotency = Substitute.For<IIdempotencyCheck>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly RequestHandler _handler;

    public RequestHandlerTests()
    {
        var options = Substitute.For<IOptionsSnapshot<RequestsOptions>>();
        options.Value.Returns(new RequestsOptions());
        _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new Client { Id = 7, ClientId = "sophia", IsActive = true });
        _idempotency.ComputeKey(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<StatsPeriod>(), Arg.Any<IReadOnlyCollection<string>>()).Returns("the-key");
        _requestService.SubmitAsync(Arg.Any<RequestSubmission>(), Arg.Any<CancellationToken>()).Returns(new SubmissionResult(SubmissionOutcome.Queued, "cid-1"));
        _handler = new RequestHandler(_clients, _idempotency, _requestService, options);
    }

    [Fact]
    public async Task Single_NormalizesTheEmail_AndAcknowledgesWithTheCorrelationId()
    {
        var result = await _handler.HandleSingleAsync("sophia", new StatsRequest("  Jane.Doe@ARBISOFT.com ", "24h"), CancellationToken.None);

        Assert.Equal(IntakeOutcome.Accepted, result.Outcome);
        Assert.Equal(new StatsAcknowledgement("200", "jane.doe@arbisoft.com", "cid-1", string.Empty), result.Acknowledgement);
        await _requestService.Received(1).SubmitAsync(
            Arg.Is<RequestSubmission>(s => s.ClientRefId == 7 && s.EventType == "engineer-stats" && s.Emails.SequenceEqual(SingleEmail)
                && s.Period.TimeFrame!.Value.Text == "24h" && s.IdempotencyKey == "the-key"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Single_WithARange_SubmitsTheRangeInUtc_AndRecordsARangeUrl()
    {
        await _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", null, "2026-11-01T05:00:00+05:00", "2026-11-08T00:00:00Z"), CancellationToken.None);

        await _requestService.Received(1).SubmitAsync(
            Arg.Is<RequestSubmission>(s => s.Period.TimeFrame == null && s.Period.From == new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
                && s.Period.To == new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero) && s.RequestUrl == "/api/v1/engineers/{email}/stats?from={from}&to={to}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Single_WithATimeFrame_RecordsATimeFrameUrl()
    {
        await _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", "24h"), CancellationToken.None);

        await _requestService.Received(1).SubmitAsync(Arg.Is<RequestSubmission>(s => s.RequestUrl == "/api/v1/engineers/{email}/stats?tf={timeFrame}"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Single_AnIdenticalRecentRequest_ReturnsTheOriginalAcknowledgementWithoutQueueingAgain()
    {
        _idempotency.FindDuplicateAsync("the-key", Arg.Any<CancellationToken>()).Returns(new RequestLog { CorrelationId = "original" });

        var result = await _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", "24h"), CancellationToken.None);

        Assert.Equal(IntakeOutcome.Accepted, result.Outcome);
        Assert.Equal("original", result.Acknowledgement!.CorrelationId);
        Assert.Equal("200", result.Acknowledgement.Status);
        await _requestService.DidNotReceiveWithAnyArgs().SubmitAsync(default!, default);
    }

    [Fact]
    public async Task Single_WithNoCallbackUrl_Returns422AcknowledgementWithTheReason()
    {
        _requestService.SubmitAsync(Arg.Any<RequestSubmission>(), Arg.Any<CancellationToken>()).Returns(new SubmissionResult(SubmissionOutcome.NoCallbackUrl, "cid-2"));

        var result = await _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", "24h"), CancellationToken.None);

        Assert.Equal(IntakeOutcome.NoCallbackUrl, result.Outcome);
        Assert.Equal(new StatsAcknowledgement("422", "jane@arbisoft.com", "cid-2", "Revoked - no callback URL"), result.Acknowledgement);
    }

    [Fact]
    public async Task Batch_NormalizesAndDeduplicatesTheEmails_KeepingTheirOrder()
    {
        var request = new BatchStatsRequest { Emails = ["B@arbisoft.com", "a@arbisoft.com", " b@Arbisoft.com"], TimeFrame = "1w" };

        var result = await _handler.HandleBatchAsync("sophia", request, CancellationToken.None);

        Assert.Equal(NormalizedBatch, result.Acknowledgement!.Emails);
        Assert.Equal("200", result.Acknowledgement.Status);
        await _requestService.Received(1).SubmitAsync(
            Arg.Is<RequestSubmission>(s => s.EventType == "engineer-stats-batch" && s.RequestUrl == "/api/v1/engineers/stats/batch"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Batch_WithNoCallbackUrl_Returns422()
    {
        _requestService.SubmitAsync(Arg.Any<RequestSubmission>(), Arg.Any<CancellationToken>()).Returns(new SubmissionResult(SubmissionOutcome.NoCallbackUrl, "cid-3"));

        var result = await _handler.HandleBatchAsync("sophia", new BatchStatsRequest { Emails = ["a@arbisoft.com"], TimeFrame = "24h" }, CancellationToken.None);

        Assert.Equal(new BatchStatsAcknowledgement("422", ["a@arbisoft.com"], "cid-3", "Revoked - no callback URL"), result.Acknowledgement, new AcknowledgementComparer());
    }

    [Theory]
    [InlineData("nobody", false)]
    [InlineData("sophia", true)]
    public async Task AnUnknownOrInactiveClient_IsNotServed(string clientId, bool inactive)
    {
        if (inactive)
        {
            _clients.GetByClientIdAsync("sophia", Arg.Any<CancellationToken>()).Returns(new Client { Id = 7, ClientId = "sophia", IsActive = false });
        }

        var result = await _handler.HandleSingleAsync(clientId, new StatsRequest("jane@arbisoft.com", "24h"), CancellationToken.None);

        Assert.Equal(IntakeOutcome.UnknownClient, result.Outcome);
        Assert.Null(result.Acknowledgement);
        await _requestService.DidNotReceiveWithAnyArgs().SubmitAsync(default!, default);
    }

    [Fact]
    public async Task AnEmailThatSkippedValidation_IsRefused_NotGuessedAt()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleSingleAsync("sophia", new StatsRequest("not-an-email", "24h"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleSingleAsync("sophia", new StatsRequest("jane@gmail.com", "24h"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", "24H"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", "24h", "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleSingleAsync("sophia", new StatsRequest("jane@arbisoft.com", null, "2026-11-02T00:00:00Z", "2026-11-01T00:00:00Z"), CancellationToken.None));
    }

    /// <summary>Compares batch acknowledgements by value, including the email list.</summary>
    private sealed class AcknowledgementComparer : IEqualityComparer<BatchStatsAcknowledgement?>
    {
        public bool Equals(BatchStatsAcknowledgement? x, BatchStatsAcknowledgement? y) =>
            x is not null && y is not null && x.Status == y.Status && x.CorrelationId == y.CorrelationId && x.Error == y.Error && x.Emails.SequenceEqual(y.Emails);

        public int GetHashCode(BatchStatsAcknowledgement? obj) => obj?.CorrelationId.GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
