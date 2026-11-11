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
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Controllers;

public sealed class RequestsApiPipelineTests : IDisposable
{
    private const string Id = "3eef2f41-18a6-45e6-b620-e11aa9c69812";

    private readonly ApiTestHost _host = new(new Dictionary<string, string?> { ["RateLimits:PollPerMinute"] = "3" });

    public RequestsApiPipelineTests()
    {
        _host.RequestLogs.GetByCorrelationIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RequestLog?)null);
        _host.RequestLogs.GetByCorrelationIdAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new RequestLogBuilder().WithCorrelationId(Id).WithStatus(RequestLogStatus.Processed).Build());
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Poll_ReturnsTheContractBody()
    {
        using var response = await _host.SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{Id}", await _host.GetTokenAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(Id, body.GetProperty("correlation_id").GetString());
        Assert.Equal("Processing", body.GetProperty("status").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", body.GetProperty("check_time").GetString());
    }

    [Theory]
    [InlineData("8d1f1a3c-0000-4000-8000-000000000000", HttpStatusCode.NotFound)]
    [InlineData("not-a-uuid", HttpStatusCode.BadRequest)]
    public async Task Poll_Returns4xxForUnknownOrInvalidIds(string id, HttpStatusCode expected)
    {
        using var response = await _host.SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{id}", await _host.GetTokenAsync());

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Poll_RequiresTheStatsReadScope()
    {
        using var anonymous = await _host.SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{Id}", null);
        _host.GrantScopes("projects:fetch");
        using var wrongScope = await _host.SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{Id}", await _host.GetTokenAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
    }

    [Fact]
    public async Task Poll_ReturnsA429OverThePerClientLimit()
    {
        var token = await _host.GetTokenAsync();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await _host.SendAsync(HttpMethod.Get, $"/api/v1/requests/poll/{Id}", token);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
