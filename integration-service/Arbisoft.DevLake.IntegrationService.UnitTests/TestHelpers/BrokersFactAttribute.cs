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

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>A fact that needs both a MySQL server and a RabbitMQ broker; it is skipped unless both test environment variables are set.</summary>
public sealed class BrokersFactAttribute : FactAttribute
{
    public BrokersFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(MySqlDatabaseFixture.ServerConnectionString) || RabbitMqHarness.Settings is null)
        {
            Skip = $"Set {MySqlDatabaseFixture.EnvironmentVariable} and {RabbitMqHarness.EnvironmentVariable} to run this test.";
        }
    }
}
