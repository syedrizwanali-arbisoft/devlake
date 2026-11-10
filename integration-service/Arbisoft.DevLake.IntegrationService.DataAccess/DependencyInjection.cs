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

using Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Settings are checked when messaging is first used, so a process that never queues doesn't need them.
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IConnectionFactory>(provider =>
            RabbitMqConnection.CreateFactory(provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddSingleton<IRequestQueuePublisher, RequestQueuePublisher>();
        services.AddSingleton<IRequestQueueConsumer, RequestQueueConsumer>();
        return services;
    }
}
