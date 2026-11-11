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
using Arbisoft.DevLake.IntegrationService.Business.Services.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;
using Arbisoft.DevLake.IntegrationService.Business.Services.Projects;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business;

public static class DependencyInjection
{
    /// <summary>Registers everything in the Business layer: services, security and DTO helpers.</summary>
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();

        services.AddOptions<RequestsOptions>()
            .Bind(configuration.GetSection(RequestsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<ISecretEncryptor, SecretEncryptor>();
        services.AddSingleton<ISecretDecryptor>(provider => provider.GetRequiredService<ISecretEncryptor>());
        services.AddSingleton<IClientSecretHasher, ClientSecretHasher>();
        services.AddScoped<ISecretStore, SecretStore>();
        services.AddScoped<ISigningKeyProvider, SigningKeyProvider>();
        services.AddScoped<ITokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IScopeService, ScopeService>();
        services.AddScoped<ISigningKeyService, SigningKeyService>();
        services.AddScoped<IAccessTokenService, AccessTokenService>();
        services.AddScoped<IEmailMappingService, EmailMappingService>();
        services.AddScoped<IClientConfigService, ClientConfigService>();
        services.AddScoped<IRequestLogService, RequestLogService>();
        services.AddScoped<IIdempotencyCheck, IdempotencyCheck>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<ICallbackAvailability, CallbackAvailability>();
        services.AddScoped<IRequestHandler, RequestHandler>();
        services.AddScoped<IRequestStatusService, RequestStatusService>();
        services.AddScoped<IErpIntegrationService, ErpIntegrationService>();

        services.AddScoped<IRequestAttemptLog, RequestAttemptLog>();
        services.AddScoped<IEngineerStatsCollector, EngineerStatsCollector>();
        services.AddScoped<ICallbackDelivery, CallbackDelivery>();
        services.AddScoped<IRequestWorker, RequestWorker>();
        services.AddScoped<IStaleRequestSweeper, StaleRequestSweeper>();
        services.AddScoped<IProcessingPreflight, ProcessingPreflight>();
        services.AddScoped<IRequestProcessor, RequestProcessor>();
        return services;
    }
}
