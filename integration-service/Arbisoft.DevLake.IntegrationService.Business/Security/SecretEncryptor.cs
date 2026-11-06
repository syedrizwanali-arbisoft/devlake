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

/// <summary>AES-256-GCM. The stored value is "v1:" followed by base64 of nonce, tag and cipher text.</summary>
public sealed class SecretEncryptor : ISecretEncryptor
{
    private const string Prefix = "v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Lazy<byte[]> _key;

    public SecretEncryptor(IOptions<SecurityOptions> options)
    {
        _key = new Lazy<byte[]>(() => ParseKey(options.Value.EncryptionKey));
    }

    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var plain = Encoding.UTF8.GetBytes(plainText);
        var payload = new byte[NonceSize + TagSize + plain.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The encrypted value has an unknown format.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(cipherText[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The encrypted value has an unknown format.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The encrypted value is too short.");
        }

        var plain = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Decrypt(
            payload.AsSpan(0, NonceSize),
            payload.AsSpan(NonceSize + TagSize),
            payload.AsSpan(NonceSize, TagSize),
            plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] ParseKey(string encodedKey)
    {
        const string message = "Security:EncryptionKey must be the base64 of a 32-byte key.";
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new InvalidOperationException(message);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(message, ex);
        }

        return key.Length == KeySize ? key : throw new InvalidOperationException(message);
    }
}
