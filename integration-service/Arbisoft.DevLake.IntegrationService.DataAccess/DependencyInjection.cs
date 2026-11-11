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
using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Polly;

namespace Arbisoft.DevLake.IntegrationService.DataAccess;

public static class DependencyInjection
{
    private const int MaxResponseBytes = 5 * 1024 * 1024;

    /// <summary>Registers everything in the DataAccess layer. Settings are validated when the host starts.</summary>
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<DatabaseOptions>(services, configuration, DatabaseOptions.SectionName);
        AddValidatedOptions<RabbitMqOptions>(services, configuration, RabbitMqOptions.SectionName);
        AddValidatedOptions<DevLakeOptions>(services, configuration, DevLakeOptions.SectionName);
        AddValidatedOptions<SophiaOptions>(services, configuration, SophiaOptions.SectionName);
        AddValidatedOptions<WorkstreamOptions>(services, configuration, WorkstreamOptions.SectionName);
        AddValidatedOptions<ResilienceOptions>(services, configuration, ResilienceOptions.SectionName);

        AddPersistence(services);
        AddMessaging(services);
        AddExternalApis(services);
        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
        services.TryAddSingleton(TimeProvider.System);

        // One pooled data source for the whole process; connections come from the factory.
        services.AddSingleton(provider =>
            new MySqlDataSourceBuilder(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString).Build());
        services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
        services.AddScoped<IDbSession, DbSession>();

        services.AddScoped<IEmailMappingRepository, EmailMappingRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IScopeRepository, ScopeRepository>();
        services.AddScoped<ISigningKeyRepository, SigningKeyRepository>();
        services.AddScoped<IAccessTokenRepository, AccessTokenRepository>();
        services.AddScoped<IRequestLogRepository, RequestLogRepository>();
        services.AddScoped<IClientConfigRepository, ClientConfigRepository>();
        services.AddScoped<ISecretStoreRepository, SecretStoreRepository>();

        // Runs first among the app's hosted services, so the schema exists before anything uses it.
        services.AddHostedService<DatabaseMigrationService>();
        services.AddHealthChecks().AddCheck<MySqlHealthCheck>("mysql", tags: ["ready"]);
    }

    private static void AddExternalApis(IServiceCollection services)
    {
        services.AddSingleton<DevLakeConcurrencyGate>();
        services.AddSingleton<SophiaTokenCache>();
        services.AddScoped<IDevLakeClient, DevLakeClient>();
        services.AddScoped<IWorkstreamClient, WorkstreamClient>();
        services.AddScoped<ISophiaClient, SophiaClient>();

        // Each client has its own timeout and circuit breaker. Read calls retry transient failures; the Sophia callback does not, because the
        // processing job retries it and records every attempt.
        AddUpstreamClient(services, ExternalHttpClients.DevLake, retries: true, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<DevLakeOptions>>().Value;
            client.BaseAddress = EndWithSlash(options.BaseUrl);
        }, provider => provider.GetRequiredService<IOptions<DevLakeOptions>>().Value.TimeoutSeconds);

        AddUpstreamClient(services, ExternalHttpClients.Workstream, retries: true, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<WorkstreamOptions>>().Value;
            client.BaseAddress = EndWithSlash(options.BaseUrl);
        }, provider => provider.GetRequiredService<IOptions<WorkstreamOptions>>().Value.TimeoutSeconds);

        AddUpstreamClient(services, ExternalHttpClients.SophiaToken, retries: true, (_, _) => { }, SophiaTimeoutSeconds);
        AddUpstreamClient(services, ExternalHttpClients.SophiaCallback, retries: false, (_, _) => { }, SophiaTimeoutSeconds);
    }

    private static int SophiaTimeoutSeconds(IServiceProvider provider) => provider.GetRequiredService<IOptions<SophiaOptions>>().Value.TimeoutSeconds;

    private static Uri EndWithSlash(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private static void AddUpstreamClient(
        IServiceCollection services, string name, bool retries, Action<IServiceProvider, HttpClient> configure, Func<IServiceProvider, int> attemptTimeoutSeconds)
    {
        services.AddHttpClient(name, (provider, client) =>
            {
                configure(provider, client);
                client.Timeout = Timeout.InfiniteTimeSpan; // The pipeline's timeouts apply instead.
                client.MaxResponseContentBufferSize = MaxResponseBytes;
            })
            .RemoveAllLoggers() // The built-in logging writes the full request URL, and a DevLake URL contains an email (SEC-P1).
            .AddResilienceHandler(name, (builder, context) =>
            {
                var resilience = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;
                var attempt = TimeSpan.FromSeconds(attemptTimeoutSeconds(context.ServiceProvider));
                if (retries && resilience.MaxRetries > 0)
                {
                    builder.AddTimeout(TotalTimeout(attempt, resilience));
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = resilience.MaxRetries,
                        Delay = TimeSpan.FromMilliseconds(resilience.RetryBaseDelayMilliseconds),
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        ShouldRetryAfterHeader = true
                    });
                }

                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = resilience.BreakerFailureRatio,
                    MinimumThroughput = resilience.BreakerMinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(resilience.BreakerSamplingSeconds),
                    BreakDuration = TimeSpan.FromSeconds(resilience.BreakerBreakSeconds)
                });
                builder.AddTimeout(attempt);
            });
    }

    /// <summary>The longest a call may take with all its retries: every attempt in full, plus the longest the backoff delays can add up to.</summary>
    private static TimeSpan TotalTimeout(TimeSpan attempt, ResilienceOptions resilience)
    {
        var longestBackoff = TimeSpan.FromMilliseconds(resilience.RetryBaseDelayMilliseconds * Math.Pow(2, resilience.MaxRetries) * 2);
        return (attempt * (resilience.MaxRetries + 1)) + longestBackoff;
    }

    private static void AddMessaging(IServiceCollection services)
    {
        // The connection is shared by the whole process; publishers and consumers open channels of their own on it.
        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddSingleton<IRequestQueuePublisher, RequestQueuePublisher>();
        services.AddSingleton<IRequestQueueConsumer, RequestQueueConsumer>();
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
    }

    private static void AddValidatedOptions<TOptions>(IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
