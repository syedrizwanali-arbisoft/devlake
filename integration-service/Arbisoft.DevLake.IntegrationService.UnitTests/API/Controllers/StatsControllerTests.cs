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

using System.Security.Claims;
using Arbisoft.DevLake.IntegrationService.API.Controllers;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Controllers;

public class StatsControllerTests
{
    private readonly IRequestHandler _handler = Substitute.For<IRequestHandler>();

    private StatsController Create()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("client_id", "sophia")], "test")),
        };
        context.Request.Path = "/api/v1/engineers/abc@arbisoft.com/stats";
        context.Request.QueryString = new QueryString("?tf=24h");
        return new StatsController(_handler) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Theory]
    [InlineData(StatsSubmissionOutcome.Accepted, 200)]
    [InlineData(StatsSubmissionOutcome.Duplicate, 200)]
    [InlineData(StatsSubmissionOutcome.Invalid, 400)]
    [InlineData(StatsSubmissionOutcome.UnknownClient, 403)]
    [InlineData(StatsSubmissionOutcome.NoCallbackUrl, 422)]
    [InlineData(StatsSubmissionOutcome.QueueUnavailable, 503)]
    public async Task GetStats_MapsEachOutcomeToItsStatus(StatsSubmissionOutcome outcome, int status)
    {
        _handler.SubmitSingleAsync("sophia", "abc@arbisoft.com", "24h", "/api/v1/engineers/abc@arbisoft.com/stats?tf=24h", Arg.Any<CancellationToken>())
            .Returns(new StatsSubmissionResult(outcome, "id", "err"));

        var result = Assert.IsType<ObjectResult>(await Create().GetStats("abc@arbisoft.com", "24h", CancellationToken.None));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(new StatsAcknowledgement(status.ToString(), "abc@arbisoft.com", "id", "err"), result.Value);
    }

    [Fact]
    public async Task GetBatchStats_EchoesTheEmails()
    {
        _handler.SubmitBatchAsync("sophia", Arg.Any<IReadOnlyList<string>?>(), "1w", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StatsSubmissionResult(StatsSubmissionOutcome.Accepted, "id", ""));

        var result = Assert.IsType<ObjectResult>(await Create().GetBatchStats(
            new BatchStatsRequest(["a@arbisoft.com", "b@arbisoft.com"], "1w"), CancellationToken.None));

        var body = Assert.IsType<BatchStatsAcknowledgement>(result.Value);
        Assert.Equal(["a@arbisoft.com", "b@arbisoft.com"], body.Emails);
        Assert.Equal("200", body.Status);
    }
}
