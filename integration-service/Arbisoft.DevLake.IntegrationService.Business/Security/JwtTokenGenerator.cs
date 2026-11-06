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

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

public sealed class JwtTokenGenerator(IOptions<JwtOptions> options) : ITokenGenerator
{
    private const string AccessTokenType = "at+jwt";

    private readonly JsonWebTokenHandler _handler = new();

    public string Generate(TokenClaims claims, SigningCredentials credentials)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = claims.IssuedOn,
            NotBefore = claims.IssuedOn,
            Expires = claims.ExpiresOn,
            TokenType = AccessTokenType,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                [TokenClaimNames.Jti] = claims.Jti,
                [JwtRegisteredClaimNames.Sub] = claims.ClientId,
                [TokenClaimNames.ClientId] = claims.ClientId,
                [TokenClaimNames.Scope] = string.Join(' ', claims.Scopes),
            },
        };
        return _handler.CreateToken(descriptor);
    }
}
