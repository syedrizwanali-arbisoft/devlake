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
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arbisoft.DevLake.IntegrationService.Business;

public static class DependencyInjection
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataAccess(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));

        services.AddSingleton<ISecretEncryptor, SecretEncryptor>();
        services.AddSingleton<ISecretHasher, SecretHasher>();
        services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
        services.AddSingleton<ISigningKeyProvider, SigningKeyProvider>();
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IAuthService, AuthService>();

        services.Configure<RequestOptions>(configuration.GetSection(RequestOptions.SectionName));
        services.AddSingleton<IIdempotencyCheck, IdempotencyCheck>();
        services.AddSingleton<IRequestService, RequestService>();
        services.AddSingleton<IRequestHandler, RequestHandler>();
        services.AddSingleton<IRequestStatusService, RequestStatusService>();
        services.AddSingleton<ICallbackTargetResolver, CallbackTargetResolver>();
        services.Configure<ProcessingOptions>(configuration.GetSection(ProcessingOptions.SectionName));
        services.AddSingleton<IRequestProcessor, RequestProcessor>();
        return services;
    }
}
