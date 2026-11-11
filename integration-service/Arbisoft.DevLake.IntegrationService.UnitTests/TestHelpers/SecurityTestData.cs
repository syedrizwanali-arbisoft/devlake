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

using System.Security.Cryptography;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Security settings with a random master key, and signing keys for tests.</summary>
public static class SecurityTestData
{
    public const string Issuer = "https://integration-service.test";
    public const string Audience = "integration-service-test";

    public static string NewEncryptionKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecurityOptionsValidator.KeySizeBytes));

    public static IOptions<SecurityOptions> Options(string? encryptionKey = null)
    {
        return Microsoft.Extensions.Options.Options.Create(new SecurityOptions
        {
            EncryptionKey = encryptionKey ?? NewEncryptionKey(),
            Issuer = Issuer,
            Audience = Audience
        });
    }
}
