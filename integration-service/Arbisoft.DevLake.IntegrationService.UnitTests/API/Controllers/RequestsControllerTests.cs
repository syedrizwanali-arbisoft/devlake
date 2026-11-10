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

public class RequestsControllerTests
{
    private readonly IRequestStatusService _service = Substitute.For<IRequestStatusService>();

    private RequestsController Create() => new(_service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("client_id", "sophia")], "test")),
            },
        },
    };

    [Fact]
    public async Task Poll_ReturnsTheStatusForTheCallingClient()
    {
        var response = new RequestStatusResponse("id", "Queued", "2026-11-05T14:02:55Z");
        _service.GetStatusAsync("sophia", "id", Arg.Any<CancellationToken>())
            .Returns(new RequestStatusResult(RequestStatusOutcome.Found, response));

        var result = await Create().Poll("id", CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData(RequestStatusOutcome.NotFound, typeof(NotFoundObjectResult))]
    [InlineData(RequestStatusOutcome.InvalidId, typeof(BadRequestObjectResult))]
    public async Task Poll_MapsFailuresTo4xx(RequestStatusOutcome outcome, Type expected)
    {
        _service.GetStatusAsync(default!, default).ReturnsForAnyArgs(new RequestStatusResult(outcome, null));

        Assert.IsType(expected, await Create().Poll("id", CancellationToken.None));
    }
}
