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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers.Builders;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

public class BuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Client_DefaultsAreAValidActiveClient_AndEachWithChangesOnlyItsOwnField()
    {
        var client = new ClientBuilder().Build();
        Assert.True(client.IsActive);
        Assert.Equal("sophia", client.ClientId);
        Assert.Null(client.ModifiedBy);

        var changed = new ClientBuilder().WithId(7).WithClientId("other").WithName("Other").Inactive().WithLifetime(60).CreatedOn(Now, "me").ModifiedOn(Now, "you").Build();
        Assert.Equal((7L, "other", "Other", false, 60), (changed.Id, changed.ClientId, changed.Name, changed.IsActive, changed.AccessTokenLifetimeSeconds));
        Assert.Equal((Now, "me", Now, "you"), (changed.CreatedOn, changed.CreatedBy, changed.ModifiedOn, changed.ModifiedBy));
    }

    [Fact]
    public void AccessToken_IsActiveForAnHourByDefault_AndCanBeRevokedOrShortened()
    {
        var token = new AccessTokenBuilder().Build();
        Assert.Equal(token.IssuedOn.AddHours(1), token.ExpiresOn);
        Assert.Null(token.RevokedOn);
        Assert.NotEqual(new AccessTokenBuilder().Build().Jti, token.Jti);

        var changed = new AccessTokenBuilder().WithId(3).WithJti("j").ForClient(9).IssuedWith(4, 5).WithScopes("a b").Issued(Now, TimeSpan.FromMinutes(5)).Revoked(Now, "leaked").Build();
        Assert.Equal((3L, "j", 9L, 4L, 5L, "a b"), (changed.Id, changed.Jti, changed.ClientRefId, changed.ClientSecretId, changed.SigningKeyId, changed.GrantedScopes));
        Assert.Equal((Now, Now.AddMinutes(5), Now, "leaked"), (changed.IssuedOn, changed.ExpiresOn, changed.RevokedOn, changed.RevokedReason));
    }

    [Fact]
    public void RequestLog_DefaultsToASuccessfulSingleRequest_AndCanBecomeABatch()
    {
        var log = new RequestLogBuilder().Build();
        Assert.Equal((RequestStatus.Success, "engineer-stats"), (log.Status, log.EventType));
        Assert.Null(log.IdempotencyKey);

        var batch = new RequestLogBuilder().WithId(2).ForClient(5).WithCorrelationId("c").WithStatus(RequestStatus.Failed).Batch().CreatedOn(Now).WithIdempotencyKey("k").Build();
        Assert.Equal(("engineer-stats-batch", "/api/v1/engineers/stats/batch", RequestStatus.Failed), (batch.EventType, batch.Url, batch.Status));
        Assert.Equal((2L, 5L, "c", Now, "k"), (batch.Id, batch.ClientRefId, batch.CorrelationId, batch.CreatedOn, batch.IdempotencyKey));
    }

    [Fact]
    public void ClientConfig_DefaultsToOAuth_AndAnApiTokenConfigClearsTheOAuthFields()
    {
        var oauth = new ClientConfigBuilder().Build();
        Assert.Equal(ClientConfigAuthType.OAuth2ClientCredentials, oauth.AuthType);
        Assert.NotNull(oauth.OutboundClientSecretEncrypted);
        Assert.Null(oauth.ApiTokenEncrypted);

        var token = new ClientConfigBuilder().WithApiToken("enc:t", Now).Build();
        Assert.Equal(ClientConfigAuthType.ApiToken, token.AuthType);
        Assert.Equal(("enc:t", Now), (token.ApiTokenEncrypted, token.TokenExpiresOn));
        Assert.Null(token.OutboundClientSecretEncrypted);
        Assert.Null(token.TokenUrl);
        Assert.Null(token.OutboundClientId);

        var changed = new ClientConfigBuilder().WithId(9).ForClient(2).WithBaseUrl("https://x").WithEncryptedSecret("enc:x").WithScope("s").WithMaxAttempts(7).Inactive().RotatedOn(null).CreatedOn(Now, "me").Build();
        Assert.Equal((9L, 2L, "https://x", "s", 7, false), (changed.Id, changed.ClientRefId, changed.BaseUrl, changed.OutboundScope, changed.MaxAttempts, changed.IsActive));
        Assert.Equal((null, Now, "me", "enc:x"), (changed.TokenRotatedOn, changed.CreatedOn, changed.CreatedBy, changed.OutboundClientSecretEncrypted));
    }
}
