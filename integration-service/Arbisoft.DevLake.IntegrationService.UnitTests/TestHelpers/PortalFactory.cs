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
using Arbisoft.DevLake.IntegrationService.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Hosts the real admin portal in-process against the test database, with one admin account.</summary>
public sealed class PortalFactory : WebApplicationFactory<WebAssemblyMarker>
{
    public const string AdminUsername = "portal-admin";
    public const string AdminPassword = "a long test password";

    private readonly string _connectionString;
    private readonly bool _configureAdmin;

    public PortalFactory(string connectionString, bool configureAdmin = true)
    {
        _connectionString = connectionString;
        _configureAdmin = configureAdmin;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString", _connectionString);
        builder.UseSetting("Security:EncryptionKey", SecurityTestData.NewEncryptionKey());
        builder.UseSetting("Security:Issuer", SecurityTestData.Issuer);
        builder.UseSetting("Security:Audience", SecurityTestData.Audience);
        // The portal never publishes or consumes, but the host validates these settings at startup and the connection is only opened on use.
        var broker = RabbitMqHarness.Settings;
        builder.UseSetting("RabbitMq:HostName", broker?["host"] ?? "localhost");
        builder.UseSetting("RabbitMq:Port", broker?["port"] ?? "5672");
        builder.UseSetting("RabbitMq:UserName", broker?["user"] ?? "user");
        builder.UseSetting("RabbitMq:Password", broker?["password"] ?? "pass");
        builder.UseSetting("DevLake:ApiToken", "token");
        builder.UseSetting("Workstream:ApiToken", "token");
        if (_configureAdmin)
        {
            builder.UseSetting("Admin:Username", AdminUsername);
            builder.UseSetting("Admin:PasswordHash", new ClientSecretHasher().Hash(AdminPassword));
        }
    }
}
