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

using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;
using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Arbisoft.DevLake.IntegrationService.DataAccess;

public static class DependencyInjection
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var connectionFactory = new MySqlConnectionFactory(
            configuration.GetConnectionString(DatabaseOptions.ConnectionStringName));
        services.AddSingleton(connectionFactory);
        services.AddSingleton<IDbConnectionFactory>(connectionFactory);
        services.AddHostedService<DatabaseMigrationService>();

        // Repositories hold no state, and every call opens its own connection.
        services.AddSingleton<IEmailMappingRepository, EmailMappingRepository>();
        services.AddSingleton<IClientRepository, ClientRepository>();
        services.AddSingleton<IScopeRepository, ScopeRepository>();
        services.AddSingleton<ISigningKeyRepository, SigningKeyRepository>();
        services.AddSingleton<IAccessTokenRepository, AccessTokenRepository>();
        services.AddSingleton<IRequestLogRepository, RequestLogRepository>();
        services.AddSingleton<IClientConfigRepository, ClientConfigRepository>();
        services.AddSingleton<ISecretStoreRepository, SecretStoreRepository>();

        // RabbitMQ settings are checked when messaging is first used, so a process that never queues doesn't need them.
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IConnectionFactory>(provider =>
            RabbitMqConnection.CreateFactory(provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddSingleton<IRabbitMqConnection>(provider => new RabbitMqConnection(
            provider.GetRequiredService<IConnectionFactory>,
            provider.GetRequiredService<IOptions<RabbitMqOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RabbitMqConnection>>()));
        services.AddSingleton<IRequestQueuePublisher, RequestQueuePublisher>();
        services.AddSingleton<IRequestQueueConsumer, RequestQueueConsumer>();

        services.AddExternalApiClients(configuration);
        return services;
    }

    private static void AddExternalApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DevLakeOptions>(configuration.GetSection(DevLakeOptions.SectionName));
        services.Configure<WorkstreamOptions>(configuration.GetSection(WorkstreamOptions.SectionName));
        services.Configure<SophiaOptions>(configuration.GetSection(SophiaOptions.SectionName));

        // The standard handler adds per-attempt and total timeouts, retries on transient failures (5xx, 408, 429,
        // network errors) with backoff, and a circuit breaker. Base URLs are checked when a client is first created.
        services.AddHttpClient<IDevLakeClient, DevLakeClient>((provider, http) =>
                http.BaseAddress = ExternalApiCalls.NormalizeBaseUrl(
                    provider.GetRequiredService<IOptions<DevLakeOptions>>().Value.BaseUrl, "DevLake:BaseUrl"))
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) => SetTotalTimeout(
                resilience, provider.GetRequiredService<IOptions<DevLakeOptions>>().Value.TimeoutSeconds));

        services.AddHttpClient<IWorkstreamClient, WorkstreamClient>((provider, http) =>
                http.BaseAddress = ExternalApiCalls.NormalizeBaseUrl(
                    provider.GetRequiredService<IOptions<WorkstreamOptions>>().Value.BaseUrl, "Workstream:BaseUrl"))
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) => SetTotalTimeout(
                resilience, provider.GetRequiredService<IOptions<WorkstreamOptions>>().Value.TimeoutSeconds));

        services.AddHttpClient(SophiaClient.TokenHttpClient)
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) =>
            {
                // A client credentials token request has no side effects, so it is safe to retry even though it is a POST.
                SetTotalTimeout(resilience, provider.GetRequiredService<IOptions<SophiaOptions>>().Value.TimeoutSeconds);
            });
        services.AddHttpClient(SophiaClient.CallbackHttpClient)
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) =>
            {
                SetTotalTimeout(resilience, provider.GetRequiredService<IOptions<SophiaOptions>>().Value.TimeoutSeconds);
                resilience.Retry.DisableForUnsafeHttpMethods();
            });
        services.AddSingleton<ISophiaClient, SophiaClient>();
    }

    private static void SetTotalTimeout(HttpStandardResilienceOptions resilience, int seconds)
    {
        var total = TimeSpan.FromSeconds(Math.Max(seconds, 1));
        resilience.TotalRequestTimeout.Timeout = total;

        // Each attempt must time out before the total does, or the options fail validation. A short total budget
        // shrinks the 10-second default per attempt to half the total, which still leaves room for a retry.
        var halfTotal = total / 2;
        if (resilience.AttemptTimeout.Timeout >= halfTotal)
        {
            resilience.AttemptTimeout.Timeout = halfTotal;
        }
    }
}
