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
using System.Text;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public class SophiaClientTests
{
    private static readonly SophiaConnection OAuth = new(7, "https://sophia.test/",
        new OAuth2ClientCredentials("/oauth2/token/", "devlake-stage", "s3cret", "stats.write"));

    private readonly FakeHttpMessageHandler _handler = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly SophiaClient _client;

    public SophiaClientTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, disposeHandler: false));
        _client = new SophiaClient(factory, Options.Create(new SophiaOptions { TokenRenewalMarginSeconds = 60 }), _time,
            NullLogger<SophiaClient>.Instance);
    }

    private FakeHttpMessageHandler TokenResponse(string token = "tok-1", int expiresIn = 3600) =>
        _handler.Respond(HttpStatusCode.OK, $"{{\"access_token\":\"{token}\",\"token_type\":\"Bearer\",\"expires_in\":{expiresIn}}}");

    [Fact]
    public async Task GetAccessToken_UsesClientCredentialsWithBasicAuth()
    {
        TokenResponse();

        Assert.Equal("tok-1", await _client.GetAccessTokenAsync(OAuth));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://sophia.test/oauth2/token/", request.Uri.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("devlake-stage:s3cret")), request.Headers["Authorization"]);
        Assert.Equal("grant_type=client_credentials&scope=stats.write", request.Body);
    }

    [Fact]
    public async Task GetAccessToken_ReusesTheTokenUntilShortlyBeforeItExpires()
    {
        TokenResponse("tok-1", expiresIn: 3600);
        TokenResponse("tok-2", expiresIn: 3600);

        Assert.Equal("tok-1", await _client.GetAccessTokenAsync(OAuth));
        _time.Advance(TimeSpan.FromMinutes(58));
        Assert.Equal("tok-1", await _client.GetAccessTokenAsync(OAuth));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("tok-2", await _client.GetAccessTokenAsync(OAuth));
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessToken_KeepsTokensSeparatePerConnection()
    {
        TokenResponse("stage");
        TokenResponse("prod");
        var prod = OAuth with { ClientConfigId = 8, Auth = new OAuth2ClientCredentials("/oauth2/token/", "devlake-prod", "p", null) };

        Assert.Equal("stage", await _client.GetAccessTokenAsync(OAuth));
        Assert.Equal("prod", await _client.GetAccessTokenAsync(prod));
        Assert.Equal("grant_type=client_credentials", _handler.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"token_type\":\"Bearer\"}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task GetAccessToken_ReportsTokenFailuresClearly(HttpStatusCode status, string body)
    {
        _handler.Respond(status, body);

        var exception = await Assert.ThrowsAsync<ExternalApiAuthenticationException>(() => _client.GetAccessTokenAsync(OAuth));
        Assert.StartsWith("Sophia:", exception.Message);
    }

    [Fact]
    public async Task GetAccessToken_ReportsAnUnreachableTokenEndpoint()
    {
        _handler.Throw(new HttpRequestException("no route"));

        var exception = await Assert.ThrowsAsync<ExternalApiAuthenticationException>(() => _client.GetAccessTokenAsync(OAuth));
        Assert.Contains("no route", exception.Message);
    }

    [Fact]
    public async Task GetAccessToken_RejectsIncompleteCredentials()
    {
        var incomplete = OAuth with { Auth = new OAuth2ClientCredentials("/oauth2/token/", "id", "", null) };

        await Assert.ThrowsAsync<ExternalApiAuthenticationException>(() => _client.GetAccessTokenAsync(incomplete));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SendCallback_PostsTheJsonWithTheBearerToken()
    {
        TokenResponse();
        _handler.Respond(HttpStatusCode.OK, "{\"received\":true}");

        var result = await _client.SendCallbackAsync(OAuth, "post", "/api/v1/devlake/callback/", "{\"a\":1}", "corr");

        Assert.Equal(new CallbackResult(true, 200, "{\"received\":true}", null), result);
        var request = _handler.Requests[1];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://sophia.test/api/v1/devlake/callback/", request.Uri.ToString());
        Assert.Equal("Bearer tok-1", request.Headers["Authorization"]);
        Assert.Equal("corr", request.Headers["X-Correlation-ID"]);
        Assert.StartsWith("application/json", request.Headers["Content-Type"]);
        Assert.Equal("{\"a\":1}", request.Body);
    }

    [Fact]
    public async Task SendCallback_GetsANewTokenOnceWhenTheCallbackRejectsIt()
    {
        TokenResponse("old");
        _handler.Respond(HttpStatusCode.Unauthorized);
        TokenResponse("new");
        _handler.Respond(HttpStatusCode.OK);

        var result = await _client.SendCallbackAsync(OAuth, "POST", "cb", "{}", "corr");

        Assert.True(result.Succeeded);
        Assert.Equal("Bearer new", _handler.Requests[3].Headers["Authorization"]);
        Assert.Equal(4, _handler.Requests.Count);
    }

    [Fact]
    public async Task SendCallback_ReturnsAFailedAttemptInsteadOfThrowing()
    {
        TokenResponse();
        _handler.Respond(HttpStatusCode.BadGateway, "upstream down");

        var result = await _client.SendCallbackAsync(OAuth, "POST", "cb", "{}", "corr");

        Assert.Equal(new CallbackResult(false, 502, "upstream down", "Callback returned 502 BadGateway."), result);
    }

    [Fact]
    public async Task SendCallback_ReturnsATransportFailureAsAFailedAttempt()
    {
        TokenResponse();
        _handler.Throw(new HttpRequestException("reset"));

        var result = await _client.SendCallbackAsync(OAuth, "POST", "cb", "{}", "corr");

        Assert.False(result.Succeeded);
        Assert.Null(result.StatusCode);
        Assert.Contains("reset", result.Error);
    }

    [Fact]
    public async Task SendCallback_UsesAStaticTokenWithoutATokenRequest()
    {
        _handler.Respond(HttpStatusCode.OK);
        var connection = new SophiaConnection(9, "https://other.test/base", new StaticApiToken("static"));

        await _client.SendCallbackAsync(connection, "PUT", "hook", "{}", "corr");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://other.test/base/hook", request.Uri.ToString());
        Assert.Equal("Bearer static", request.Headers["Authorization"]);
    }

    [Fact]
    public void Credentials_KeepTheSecretOutOfToString()
    {
        Assert.DoesNotContain("s3cret", OAuth.ToString());
        Assert.DoesNotContain("static", new StaticApiToken("static").ToString());
    }
}
