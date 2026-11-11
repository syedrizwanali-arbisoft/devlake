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
using System.Text;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

/// <summary>
/// AES-256-GCM with a random nonce per value. The stored form is <c>v1.</c> followed by base64 of nonce, tag and ciphertext;
/// the version prefix leaves room to change the scheme later.
/// </summary>
public sealed class SecretEncryptor : ISecretEncryptor
{
    private const string VersionPrefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretEncryptor(IOptions<SecurityOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.EncryptionKey);
    }

    public string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + TagSize + plainBytes.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return VersionPrefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string protectedValue)
    {
        if (!protectedValue.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The value is not in a supported encrypted format.");
        }

        var payload = Convert.FromBase64String(protectedValue[VersionPrefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The encrypted value is too short.");
        }

        var plainBytes = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
