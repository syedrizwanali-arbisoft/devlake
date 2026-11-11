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

using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arbisoft.DevLake.IntegrationService.DataAccess;

public static class DependencyInjection
{
    /// <summary>Registers everything in the DataAccess layer. Settings are validated when the host starts.</summary>
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<DatabaseOptions>(services, configuration, DatabaseOptions.SectionName);
        AddValidatedOptions<RabbitMqOptions>(services, configuration, RabbitMqOptions.SectionName);
        AddValidatedOptions<DevLakeOptions>(services, configuration, DevLakeOptions.SectionName);
        AddValidatedOptions<SophiaOptions>(services, configuration, SophiaOptions.SectionName);
        AddValidatedOptions<WorkstreamOptions>(services, configuration, WorkstreamOptions.SectionName);
        return services;
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
