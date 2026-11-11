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

using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.API.Authorization;

/// <summary>
/// Validates a JWT with the public key its <c>kid</c> names, looked up asynchronously, including retired keys so tokens signed
/// before a rotation keep working. The stock key resolver is synchronous, which would block a thread on every database lookup.
/// </summary>
public sealed class SigningKeyResolvingTokenHandler : JsonWebTokenHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SigningKeyResolvingTokenHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public override async Task<TokenValidationResult> ValidateTokenAsync(string token, TokenValidationParameters validationParameters)
    {
        if (!CanReadToken(token))
        {
            return Invalid(new SecurityTokenMalformedException("The token is not a well-formed JWT."));
        }

        var kid = ReadJsonWebToken(token).Kid;
        if (string.IsNullOrEmpty(kid))
        {
            return Invalid(new SecurityTokenSignatureKeyNotFoundException("The token has no kid."));
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var key = await scope.ServiceProvider.GetRequiredService<ISigningKeyProvider>().GetVerificationKeyAsync(kid, CancellationToken.None);
        if (key is null)
        {
            return Invalid(new SecurityTokenSignatureKeyNotFoundException("No signing key matches the token's kid."));
        }

        var parameters = validationParameters.Clone();
        parameters.IssuerSigningKey = key;
        return await base.ValidateTokenAsync(token, parameters);
    }

    private static TokenValidationResult Invalid(Exception exception) => new() { IsValid = false, Exception = exception };
}
