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

using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

/// <summary>Fails startup when the master key is not a base64 AES-256 key, instead of failing on the first encryption.</summary>
public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    public const int KeySizeBytes = 32;

    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        if (string.IsNullOrEmpty(options.EncryptionKey))
        {
            return ValidateOptionsResult.Success;
        }

        var buffer = new byte[KeySizeBytes + 1];
        var isBase64 = Convert.TryFromBase64String(options.EncryptionKey, buffer, out var written);
        return isBase64 && written == KeySizeBytes
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Security:EncryptionKey must be {KeySizeBytes} bytes encoded as base64.");
    }
}
