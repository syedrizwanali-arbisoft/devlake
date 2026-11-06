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

public static class ApiSecurityExtensions
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
        services.AddSingleton<SigningKeyConfigurationManager>();
        services.AddSingleton<TokenRevocationValidator>();
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, SigningKeyConfigurationManager>((options, jwt, configurationManager) =>
            {
                options.MapInboundClaims = false;
                options.ConfigurationManager = configurationManager;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = TokenClaimNames.ClientId,
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices.GetRequiredService<TokenRevocationValidator>();
                        if (!await validator.IsActiveAsync(context.Principal, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The token has been revoked.");
                        }
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            foreach (var scope in ApiScopes.All)
            {
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement(scope)));
            }
        });
        return services;
    }
}
