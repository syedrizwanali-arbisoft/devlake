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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.API.Authorization;

public static class AuthenticationConfiguration
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    private static readonly string[] AllowedAlgorithms = [RsaKeyPair.Algorithm];
    private static readonly string[] ScopePolicies = [ScopeNames.StatsRead, ScopeNames.ProjectsFetch];

    /// <summary>
    /// Adds bearer-token authentication, the revocation check, one authorization policy per scope, and a fallback policy that denies
    /// every endpoint without an explicit <c>[Authorize]</c> or <c>[AllowAnonymous]</c> (SEC-A2).
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddScoped<TokenRevocationValidator>();
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceScopeFactory, IOptions<SecurityOptions>>(ConfigureBearer);

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var scope in ScopePolicies)
            {
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement(scope)));
            }
        });
        return services;
    }

    private static void ConfigureBearer(JwtBearerOptions options, IServiceScopeFactory scopeFactory, IOptions<SecurityOptions> security)
    {
        options.MapInboundClaims = false;
        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(new SigningKeyResolvingTokenHandler(scopeFactory));
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = security.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = security.Value.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = AllowedAlgorithms,
            ClockSkew = ClockSkew
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context => context.HttpContext.RequestServices.GetRequiredService<TokenRevocationValidator>().ValidateAsync(context)
        };
    }
}
