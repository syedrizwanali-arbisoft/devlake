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
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public sealed class WorkstreamClientTests : IDisposable
{
    private const string CorrelationId = "0b8f3c52-1111-4222-8333-444455556666";

    private readonly ExternalApiHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
    }

    private async Task<T> UseAsync<T>(Func<IWorkstreamClient, Task<T>> call)
    {
        await using var scope = _host.Scope();
        return await call(scope.ServiceProvider.GetRequiredService<IWorkstreamClient>());
    }

    [Fact]
    public async Task IsTokenValid_SendsTheToken_AndReadsTrue()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "true");

        Assert.True(await UseAsync(client => client.IsTokenValidAsync(CorrelationId, CancellationToken.None)));

        var request = Assert.Single(_host.Handler.Requests);
        Assert.Equal("https://workstream.test/api/is_valid", request.AbsoluteUri);
        Assert.Equal("Bearer workstream-token-value", request.Authorization);
        Assert.Equal(CorrelationId, request.CorrelationId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.OK, "false")]
    public async Task IsTokenValid_IsFalse_WhenWorkstreamRefusesTheToken(HttpStatusCode status, string body)
    {
        _host.Handler.Respond(status, body);

        Assert.False(await UseAsync(client => client.IsTokenValidAsync(CorrelationId, CancellationToken.None)));
    }

    [Fact]
    public async Task GetProjectsPage_AsksForThePageAndSize_AndReadsABareArray()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]");

        var page = await UseAsync(client => client.GetProjectsPageAsync(2, 10, CorrelationId, CancellationToken.None));

        Assert.Equal("https://workstream.test/api/projects?page=2&pageSize=10", Assert.Single(_host.Handler.Requests).AbsoluteUri);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal([1, 2], page.Items.Select(item => item.GetProperty("id").GetInt32()));
        Assert.True(page.IsLast);
    }

    [Theory]
    [InlineData("results")]
    [InlineData("data")]
    [InlineData("items")]
    [InlineData("projects")]
    public async Task GetProjectsPage_ReadsTheListFromAnObjectToo(string property)
    {
        _host.Handler.Respond(HttpStatusCode.OK, $"{{\"{property}\":[{{\"id\":7}}],\"total\":1}}");

        var page = await UseAsync(client => client.GetProjectsPageAsync(1, 1, CorrelationId, CancellationToken.None));

        Assert.Equal(7, Assert.Single(page.Items).GetProperty("id").GetInt32());
        Assert.False(page.IsLast);
    }

    [Fact]
    public async Task AFullPage_IsNotTheLast_AndAShortOrEmptyPageIs()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]").Respond(HttpStatusCode.OK, "[{\"id\":3}]").Respond(HttpStatusCode.OK, "[]");

        var full = await UseAsync(client => client.GetProjectsPageAsync(1, 2, CorrelationId, CancellationToken.None));
        var short1 = await UseAsync(client => client.GetProjectsPageAsync(2, 2, CorrelationId, CancellationToken.None));
        var empty = await UseAsync(client => client.GetProjectsPageAsync(3, 2, CorrelationId, CancellationToken.None));

        Assert.False(full.IsLast);
        Assert.True(short1.IsLast);
        Assert.True(empty.IsLast);
        Assert.Empty(empty.Items);
    }

    [Theory]
    [InlineData("{\"message\":\"no list here\"}")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("{\"results\":\"not an array\"}")]
    public async Task GetProjectsPage_AnAnswerWithoutAList_IsAResponseError(string body)
    {
        _host.Handler.Respond(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<ExternalApiResponseException>(() => UseAsync(client => client.GetProjectsPageAsync(1, 10, CorrelationId, CancellationToken.None)));
    }

    [Fact]
    public async Task GetProjectsPage_WhenWorkstreamRejectsTheToken_ThrowsAuthentication_WithoutRetrying()
    {
        _host.Handler.Respond(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<ExternalAuthenticationException>(() => UseAsync(client => client.GetProjectsPageAsync(1, 10, CorrelationId, CancellationToken.None)));

        Assert.Single(_host.Handler.Requests);
    }

    [Fact]
    public async Task GetProjectsPage_RetriesTransientFailures_AndThenReportsUnavailable()
    {
        _host.Handler.Respond(HttpStatusCode.BadGateway);

        var exception = await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => UseAsync(client => client.GetProjectsPageAsync(1, 10, CorrelationId, CancellationToken.None)));

        Assert.Equal("Workstream", exception.SystemName);
        Assert.Equal(4, _host.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetProjectsPage_ARetryThatSucceeds_ReturnsThePage()
    {
        _host.Handler.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK, "[{\"id\":9}]");

        var page = await UseAsync(client => client.GetProjectsPageAsync(1, 10, CorrelationId, CancellationToken.None));

        Assert.Equal(9, Assert.Single(page.Items).GetProperty("id").GetInt32());
        Assert.Equal(2, _host.Handler.Requests.Count);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, 10)]
    public async Task GetProjectsPage_RefusesAPageOrSizeBelowOne(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => UseAsync(client => client.GetProjectsPageAsync(page, pageSize, CorrelationId, CancellationToken.None)));

        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public void FromJson_KeepsTheItemsValidAfterTheDocumentIsGone()
    {
        JsonElement body;
        using (var document = JsonDocument.Parse("{\"results\":[{\"id\":5}]}"))
        {
            body = document.RootElement.Clone();
        }

        Assert.Equal(5, Assert.Single(WorkstreamProjectsPage.FromJson(body, 1, 10)!.Items).GetProperty("id").GetInt32());
    }
}
