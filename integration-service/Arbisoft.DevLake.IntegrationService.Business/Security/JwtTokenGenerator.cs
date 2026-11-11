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

/// <summary>Issues RS256 JWTs with the signing key's kid in the header and jti, client, scope and expiry in the claims.</summary>
public sealed class JwtTokenGenerator : ITokenGenerator
{
    private readonly ISigningKeyProvider _signingKeys;
    private readonly SecurityOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenGenerator(ISigningKeyProvider signingKeys, IOptions<SecurityOptions> options, TimeProvider timeProvider)
    {
        _signingKeys = signingKeys;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<IssuedToken> GenerateAsync(TokenSpecification specification, CancellationToken cancellationToken)
    {
        var signingKey = await _signingKeys.GetActiveKeyAsync(cancellationToken);
        var issuedOn = _timeProvider.GetUtcNow();
        var expiresOn = issuedOn.AddSeconds(specification.LifetimeSeconds);
        var jti = Guid.NewGuid().ToString("D");

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedOn.UtcDateTime,
            NotBefore = issuedOn.UtcDateTime,
            Expires = expiresOn.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = jti,
                [JwtRegisteredClaimNames.Sub] = specification.ClientId,
                [TokenClaims.Client] = specification.ClientId,
                [TokenClaims.Scope] = string.Join(' ', specification.Scopes)
            },
            SigningCredentials = new SigningCredentials(signingKey.Key, RsaKeyPair.Algorithm)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new IssuedToken(token, jti, signingKey.Id, issuedOn, expiresOn);
    }
}
