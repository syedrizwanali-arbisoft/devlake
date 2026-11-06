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

using System.Security.Cryptography;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class JwtTokenGeneratorTests
{
    private readonly JwtTokenGenerator _generator = new(Options.Create(new JwtOptions { Issuer = "issuer", Audience = "audience" }));

    [Fact]
    public void Generate_PutsKidInTheHeaderAndClaimsInThePayload()
    {
        var issuedOn = DateTime.UtcNow.AddMinutes(-1);
        var expiresOn = issuedOn.AddHours(1);
        using var rsa = RSA.Create(2048);
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "key-1" }, SecurityAlgorithms.RsaSha256);

        var token = new JsonWebToken(_generator.Generate(
            new TokenClaims("the-jti", "sophia", ["projects:fetch", "stats:read"], issuedOn, expiresOn), credentials));

        Assert.Equal("key-1", token.Kid);
        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Alg);
        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal("the-jti", token.Id);
        Assert.Equal("issuer", token.Issuer);
        Assert.Equal(["audience"], token.Audiences);
        Assert.Equal("sophia", token.GetClaim(TokenClaimNames.ClientId).Value);
        Assert.Equal("projects:fetch stats:read", token.GetClaim(TokenClaimNames.Scope).Value);
        Assert.Equal(expiresOn, token.ValidTo, TimeSpan.FromSeconds(1));
    }
}
