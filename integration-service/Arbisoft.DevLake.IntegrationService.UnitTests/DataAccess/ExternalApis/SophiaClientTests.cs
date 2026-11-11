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
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public sealed class SophiaClientTests : IDisposable
{
    private const string CorrelationId = "0b8f3c52-1111-4222-8333-444455556666";
    private const string EventType = "engineer-stats";
    private const string Payload = "{\"correlation_id\":\"0b8f3c52\",\"engineers\":[]}";
    private const string TokenUrl = "https://sophia.test/oauth2/token/";
    private const string CallbackUrl = "https://sophia.test/api/v1/devlake/callback/";

    private readonly ExternalApiHost _host = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly SecretEncryptor _encryptor = new(SecurityTestData.Options());
    private readonly SophiaTokenCache _cache;
    private readonly SophiaClient _client;
    private int _tokensIssued;

    public SophiaClientTests()
    {
        _cache = new SophiaTokenCache(_time);
        _client = new SophiaClient(_host.HttpClientFactory, _host.Configs, _encryptor, _cache, _time, _host.Logger<SophiaClient>());
        GivenConfig(OAuthConfig());
        GivenCallbackUrl("POST", "/api/v1/devlake/callback/");
        Script((request, _) => Task.FromResult(Route(request, callbackStatus: HttpStatusCode.OK)));
    }

    public void Dispose()
    {
        _cache.Dispose();
        _host.Dispose();
    }

    /// <summary>Replaces what the fake network answers with, from the next request on.</summary>
    private void Script(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) => _host.Handler.Reset().Then(answer);

    private ClientConfig OAuthConfig(string baseUrl = "https://sophia.test", string tokenUrl = TokenUrl, string? scope = null, DateTimeOffset? modifiedOn = null) => new()
    {
        Id = 3,
        ClientRefId = 7,
        AuthType = ClientConfigAuthType.OAuth2ClientCredentials,
        BaseUrl = baseUrl,
        TokenUrl = tokenUrl,
        OutboundClientId = "stage-client-id",
        OutboundClientSecretEncrypted = _encryptor.Encrypt("stage-client-secret"),
        OutboundScope = scope,
        MaxAttempts = 3,
        IsActive = true,
        CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ModifiedOn = modifiedOn
    };

    private void GivenConfig(ClientConfig? config) => _host.Configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(config);

    private void GivenCallbackUrl(string method, string path, bool exists = true)
    {
        var url = exists ? new ClientConfigOutboundUrl { Id = 9, ClientConfigId = 3, EventType = EventType, Method = method, Path = path, IsActive = true } : null;
        _host.Configs.GetActiveOutboundUrlAsync(3, EventType, Arg.Any<CancellationToken>()).Returns(url);
    }

    /// <summary>Answers the token endpoint with a new token each time, and the callback with the given status.</summary>
    private HttpResponseMessage Route(HttpRequestMessage request, HttpStatusCode callbackStatus, string callbackBody = "{\"ok\":true}", int expiresIn = 3600)
    {
        if (request.RequestUri!.AbsoluteUri == TokenUrl)
        {
            var token = $"tok-{Interlocked.Increment(ref _tokensIssued)}";
            return Json(HttpStatusCode.OK, $"{{\"access_token\":\"{token}\",\"token_type\":\"Bearer\",\"expires_in\":{expiresIn}}}");
        }

        return Json(callbackStatus, callbackBody);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private IEnumerable<RecordedRequest> TokenRequests => _host.Handler.Requests.Where(r => r.AbsoluteUri == TokenUrl);

    private IEnumerable<RecordedRequest> CallbackRequests => _host.Handler.Requests.Where(r => r.AbsoluteUri != TokenUrl);

    private Task<SophiaDeliveryResult> PostAsync() => _client.PostCallbackAsync(7, EventType, Payload, CorrelationId, CancellationToken.None);

    [Fact]
    public async Task APost_FetchesATokenWithClientCredentials_ThenPostsTheResultWithIt()
    {
        var result = await PostAsync();

        Assert.True(result.Delivered);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("{\"ok\":true}", result.ResponseBody);

        var token = Assert.Single(TokenRequests);
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Equal("grant_type=client_credentials", token.Content);
        Assert.Equal("application/x-www-form-urlencoded", token.ContentType);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("stage-client-id:stage-client-secret")), token.Authorization);
        Assert.Equal(CorrelationId, token.CorrelationId);

        var callback = Assert.Single(CallbackRequests);
        Assert.Equal(HttpMethod.Post, callback.Method);
        Assert.Equal(CallbackUrl, callback.AbsoluteUri);
        Assert.Equal("Bearer tok-1", callback.Authorization);
        Assert.Equal(Payload, callback.Content);
        Assert.Equal("application/json", callback.ContentType);
        Assert.Equal(CorrelationId, callback.CorrelationId);
    }

    [Fact]
    public async Task EnsureAccessToken_FetchesAToken_ThatTheNextCallbackReuses()
    {
        await _client.EnsureAccessTokenAsync(7, CorrelationId, CancellationToken.None);
        await PostAsync();

        Assert.Single(TokenRequests);
        Assert.Equal("Bearer tok-1", Assert.Single(CallbackRequests).Authorization);
    }

    [Fact]
    public async Task EnsureAccessToken_WhenSophiaRefusesTheCredentials_ThrowsAuthentication_AndSendsNothingElse()
    {
        Script((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));

        await Assert.ThrowsAsync<ExternalAuthenticationException>(() => _client.EnsureAccessTokenAsync(7, CorrelationId, CancellationToken.None));

        Assert.Empty(CallbackRequests);
    }

    [Fact]
    public async Task EnsureAccessToken_ForAClientWithNoConfig_IsNotConfigured()
    {
        GivenConfig(null);

        await Assert.ThrowsAsync<CallbackNotConfiguredException>(() => _client.EnsureAccessTokenAsync(7, CorrelationId, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureAccessToken_ForAnApiTokenConfig_NeedsNoFetch_ButRefusesAnExpiredToken()
    {
        GivenConfig(new ClientConfig
        {
            Id = 3,
            ClientRefId = 7,
            AuthType = ClientConfigAuthType.ApiToken,
            BaseUrl = "https://sophia.test",
            ApiTokenEncrypted = _encryptor.Encrypt("static-token"),
            TokenExpiresOn = _time.GetUtcNow().AddDays(1),
            IsActive = true
        });
        await _client.EnsureAccessTokenAsync(7, CorrelationId, CancellationToken.None);
        Assert.Empty(_host.Handler.Requests);

        _time.Advance(TimeSpan.FromDays(2));
        await Assert.ThrowsAsync<ExternalAuthenticationException>(() => _client.EnsureAccessTokenAsync(7, CorrelationId, CancellationToken.None));
    }

    [Fact]
    public async Task TheConfiguredScope_IsSentWithTheTokenRequest()
    {
        GivenConfig(OAuthConfig(scope: "callbacks.write"));

        await PostAsync();

        Assert.Equal("grant_type=client_credentials&scope=callbacks.write", Assert.Single(TokenRequests).Content);
    }

    [Fact]
    public async Task TheMethodAndPath_ComeFromTheOutboundUrlRow()
    {
        GivenCallbackUrl("put", "/hooks/stats/");

        await PostAsync();

        var callback = Assert.Single(CallbackRequests);
        Assert.Equal(HttpMethod.Put, callback.Method);
        Assert.Equal("https://sophia.test/hooks/stats/", callback.AbsoluteUri);
    }

    [Fact]
    public async Task ATokenIsReusedUntilShortlyBeforeItExpires()
    {
        await PostAsync();
        _time.Advance(TimeSpan.FromSeconds(3539));
        await PostAsync();

        Assert.Single(TokenRequests);
        Assert.Equal(2, CallbackRequests.Count());

        _time.Advance(TimeSpan.FromSeconds(2));
        await PostAsync();

        Assert.Equal(2, TokenRequests.Count());
        Assert.Equal("Bearer tok-2", CallbackRequests.Last().Authorization);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    public async Task ATokenThatLastsNoLongerThanTheSkew_IsNeverReused(int expiresIn)
    {
        Script((request, _) => Task.FromResult(Route(request, HttpStatusCode.OK, expiresIn: expiresIn)));

        await PostAsync();
        await PostAsync();

        Assert.Equal(2, TokenRequests.Count());
    }

    [Fact]
    public async Task ATokenWithoutExpiresIn_IsCachedForAShortDefaultTime()
    {
        Script((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == TokenUrl
            ? Json(HttpStatusCode.OK, "{\"access_token\":\"tok-x\"}")
            : Json(HttpStatusCode.OK, "{}")));

        await PostAsync();
        await PostAsync();
        Assert.Single(TokenRequests);

        _time.Advance(TimeSpan.FromMinutes(5));
        await PostAsync();
        Assert.Equal(2, TokenRequests.Count());
    }

    [Fact]
    public async Task ManyCallbacksAtOnce_MakeOneTokenRequest()
    {
        Script(async (request, token) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            return Route(request, HttpStatusCode.OK);
        });

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync()));

        Assert.Single(TokenRequests);
        Assert.Equal(6, CallbackRequests.Count());
    }

    [Fact]
    public async Task AChangedConfig_IsNotServedAnOldToken()
    {
        var before = OAuthConfig(modifiedOn: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var after = OAuthConfig(modifiedOn: new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
        _host.Configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(before, after);

        await PostAsync();
        await PostAsync();

        Assert.Equal(2, TokenRequests.Count());
    }

    [Fact]
    public async Task WhenSophiaRejectsTheCachedToken_ItIsReplacedOnce_AndTheCallIsRepeated()
    {
        await PostAsync();
        var callbacksSeen = 0;
        Script((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == TokenUrl
            ? Route(request, HttpStatusCode.OK)
            : Interlocked.Increment(ref callbacksSeen) == 1 ? Json(HttpStatusCode.Unauthorized, "{}") : Json(HttpStatusCode.OK, "{\"ok\":true}")));

        var result = await PostAsync();

        Assert.True(result.Delivered);
        Assert.Equal(2, TokenRequests.Count());
        Assert.Equal(["Bearer tok-1", "Bearer tok-1", "Bearer tok-2"], CallbackRequests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task WhenSophiaRejectsEvenAFreshToken_TheFailureIsAnAuthenticationError()
    {
        Script((request, _) => Task.FromResult(Route(request, HttpStatusCode.Unauthorized)));

        var exception = await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal(2, CallbackRequests.Count());
        Assert.Equal(2, TokenRequests.Count());
    }

    [Fact]
    public async Task AForbiddenCallback_IsAnAuthenticationError_WithoutFetchingAnotherToken()
    {
        Script((request, _) => Task.FromResult(Route(request, HttpStatusCode.Forbidden)));

        await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Assert.Single(CallbackRequests);
        Assert.Single(TokenRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task WhenTheTokenEndpointRefusesTheCredentials_ItIsAnAuthenticationError_WithoutRetrying(HttpStatusCode status)
    {
        Script((_, _) => Task.FromResult(Json(status, "{\"error\":\"invalid_client\"}")));

        var exception = await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Assert.Contains("Sophia", exception.Message, StringComparison.Ordinal);
        Assert.Single(_host.Handler.Requests);
    }

    [Fact]
    public async Task AnUnavailableTokenEndpoint_IsRetried_ThenReportedUnavailable_AndNothingIsPosted()
    {
        Script((_, _) => Task.FromResult(Json(HttpStatusCode.BadGateway, "{}")));

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(PostAsync);

        Assert.Equal(4, TokenRequests.Count());
        Assert.Empty(CallbackRequests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"\"}")]
    [InlineData("{\"access_token\":123}")]
    [InlineData("[]")]
    public async Task ATokenResponseWithoutAnAccessToken_IsAnAuthenticationError(string body)
    {
        Script((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));

        var exception = await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Assert.Contains("access_token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedTokenFetch_IsNotCached_SoTheNextCallTriesAgain()
    {
        Script((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));
        await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Script((request, _) => Task.FromResult(Route(request, HttpStatusCode.OK)));
        var result = await PostAsync();

        Assert.True(result.Delivered);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{\"error\":\"unknown engineer\"}")]
    [InlineData(HttpStatusCode.BadRequest, "bad")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.TooManyRequests, "slow down")]
    public async Task AnyOtherAnswer_IsAResultToRecord_AndIsNeverRetriedHere(HttpStatusCode status, string body)
    {
        Script((request, _) => Task.FromResult(Route(request, status, body)));

        var result = await PostAsync();

        Assert.False(result.Delivered);
        Assert.Equal((int)status, result.StatusCode);
        Assert.Equal(body, result.ResponseBody);
        Assert.Single(CallbackRequests);
    }

    [Fact]
    public async Task AVeryLongAnswer_IsCutToALengthThatIsSafeToStore()
    {
        Script((request, _) => Task.FromResult(Route(request, HttpStatusCode.InternalServerError, new string('x', 20_000))));

        var result = await PostAsync();

        Assert.Equal(8192, result.ResponseBody.Length);
    }

    [Fact]
    public async Task WhenSophiaCannotBeReached_TheCallbackIsNotRetriedBlindly()
    {
        Script((request, _) => request.RequestUri!.AbsoluteUri == TokenUrl
            ? Task.FromResult(Route(request, HttpStatusCode.OK))
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("connection reset")));

        var exception = await Assert.ThrowsAsync<ExternalApiUnavailableException>(PostAsync);

        Assert.Equal("Sophia", exception.SystemName);
        Assert.Single(CallbackRequests);
    }

    [Fact]
    public async Task ASlowSophia_TimesOutAndIsReportedUnavailable()
    {
        using var host = new ExternalApiHost(new Dictionary<string, string?> { ["Sophia:TimeoutSeconds"] = "1" });
        host.Configs.GetByClientRefIdAsync(7, Arg.Any<CancellationToken>()).Returns(OAuthConfig());
        host.Configs.GetActiveOutboundUrlAsync(3, EventType, Arg.Any<CancellationToken>()).Returns(new ClientConfigOutboundUrl { Id = 9, ClientConfigId = 3, EventType = EventType, Method = "POST", Path = "/cb/", IsActive = true });
        host.Handler.Then((request, token) => request.RequestUri!.AbsoluteUri == TokenUrl
            ? Task.FromResult(Route(request, HttpStatusCode.OK))
            : Task.Delay(TimeSpan.FromSeconds(30), token).ContinueWith(_ => Json(HttpStatusCode.OK, "{}"), token));
        using var cache = new SophiaTokenCache(_time);
        var client = new SophiaClient(host.HttpClientFactory, host.Configs, _encryptor, cache, _time, host.Logger<SophiaClient>());

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(() => client.PostCallbackAsync(7, EventType, Payload, CorrelationId, CancellationToken.None));
    }

    [Fact]
    public async Task AnApiTokenConfig_SendsTheStoredTokenAsABearer_WithoutAskingForOne()
    {
        GivenConfig(new ClientConfig
        {
            Id = 3,
            ClientRefId = 7,
            AuthType = ClientConfigAuthType.ApiToken,
            BaseUrl = "https://sophia.test",
            ApiTokenEncrypted = _encryptor.Encrypt("static-token"),
            TokenExpiresOn = _time.GetUtcNow().AddDays(30),
            IsActive = true
        });

        var result = await PostAsync();

        Assert.True(result.Delivered);
        Assert.Empty(TokenRequests);
        Assert.Equal("Bearer static-token", Assert.Single(CallbackRequests).Authorization);
    }

    [Fact]
    public async Task AnExpiredStoredApiToken_IsReportedClearly_AndNothingIsSent()
    {
        GivenConfig(new ClientConfig
        {
            Id = 3,
            ClientRefId = 7,
            AuthType = ClientConfigAuthType.ApiToken,
            BaseUrl = "https://sophia.test",
            ApiTokenEncrypted = _encryptor.Encrypt("static-token"),
            TokenExpiresOn = _time.GetUtcNow().AddSeconds(-1),
            IsActive = true
        });

        var exception = await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Assert.Contains("expired", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public async Task ASecretThatCannotBeDecrypted_IsAnAuthenticationError_NotACrash()
    {
        var other = new SecretEncryptor(SecurityTestData.Options());
        GivenConfig(new ClientConfig
        {
            Id = 3,
            ClientRefId = 7,
            AuthType = ClientConfigAuthType.OAuth2ClientCredentials,
            BaseUrl = "https://sophia.test",
            TokenUrl = TokenUrl,
            OutboundClientId = "id",
            OutboundClientSecretEncrypted = other.Encrypt("secret"),
            IsActive = true
        });

        var exception = await Assert.ThrowsAsync<ExternalAuthenticationException>(PostAsync);

        Assert.Contains("decrypted", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public async Task WithoutAnActiveConfig_OrCallbackUrl_ThereIsNowhereToSend()
    {
        GivenConfig(null);
        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        var inactive = OAuthConfig();
        GivenConfig(new ClientConfig { Id = inactive.Id, ClientRefId = 7, AuthType = inactive.AuthType, BaseUrl = inactive.BaseUrl, IsActive = false });
        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        GivenConfig(OAuthConfig());
        GivenCallbackUrl("POST", "/x/", exists: false);
        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("GET")]
    [InlineData("")]
    public async Task OnlyPostPutAndPatch_AreAllowedCallbackMethods(string method)
    {
        GivenCallbackUrl(method, "/x/");

        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("http://sophia.test")]
    [InlineData("ftp://sophia.test")]
    [InlineData("sophia.test")]
    [InlineData("")]
    public async Task ABaseUrlThatIsNotHttps_IsRefused_BeforeAnythingIsSent(string baseUrl)
    {
        GivenConfig(OAuthConfig(baseUrl: baseUrl));

        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public async Task AnInsecureTokenUrl_IsRefused_SoTheSecretIsNeverSentInTheClear()
    {
        GivenConfig(OAuthConfig(tokenUrl: "http://sophia.test/oauth2/token/"));

        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("//evil.example/steal")]
    [InlineData("no-leading-slash")]
    [InlineData("/ok/..\\..\\x")]
    [InlineData("")]
    public async Task ACallbackPath_CannotLeadAnywhereButTheConfiguredHost(string path)
    {
        GivenCallbackUrl("POST", path);

        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);

        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public async Task AnIncompleteOAuthConfig_IsReportedAsNotConfigured()
    {
        GivenConfig(new ClientConfig { Id = 3, ClientRefId = 7, AuthType = ClientConfigAuthType.OAuth2ClientCredentials, BaseUrl = "https://sophia.test", IsActive = true });

        await Assert.ThrowsAsync<CallbackNotConfiguredException>(PostAsync);
    }

    [Fact]
    public async Task LoopbackHttp_IsAllowed_ForLocalDevelopmentStubs()
    {
        GivenConfig(OAuthConfig(baseUrl: "http://localhost:5097", tokenUrl: "http://127.0.0.1:5097/oauth2/token/"));
        Script((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/oauth2/token/"
            ? Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}")
            : Json(HttpStatusCode.OK, "{}")));

        Assert.True((await PostAsync()).Delivered);
    }

    [Fact]
    public async Task NeitherTheSecretNorTheTokenNorThePayloadIsEverLogged()
    {
        Script((request, _) => Task.FromResult(Route(request, HttpStatusCode.InternalServerError, "{\"leak\":\"stage-client-secret tok-1\"}")));
        await PostAsync();
        Script((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));
        await Assert.ThrowsAsync<ExternalAuthenticationException>(() => _client.PostCallbackAsync(7, EventType, Payload, CorrelationId, CancellationToken.None));

        Assert.Contains(CorrelationId, _host.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain("stage-client-secret", _host.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-1", _host.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain("engineers", _host.Logs.All, StringComparison.Ordinal);
    }
}
