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

/// <summary>
/// A test that needs a real MySQL server. It is skipped unless INTEGRATION_TEST_MYSQL holds a connection string for
/// a user that can create and drop databases, for example the compose MySQL's root user. CI sets it.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "INTEGRATION_TEST_MYSQL";

    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            Skip = $"Set {EnvironmentVariable} to run the MySQL repository tests.";
        }
    }
}
