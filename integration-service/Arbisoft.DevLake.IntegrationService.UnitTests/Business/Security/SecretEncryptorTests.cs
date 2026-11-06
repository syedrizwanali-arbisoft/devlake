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

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class SecretEncryptorTests
{
    private static SecretEncryptor Create(string? key = null) => new(Options.Create(new SecurityOptions
    {
        EncryptionKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    }));

    [Fact]
    public void Decrypt_ReturnsTheOriginalValue()
    {
        var encryptor = Create();

        var encrypted = encryptor.Encrypt("outbound-secret");

        Assert.DoesNotContain("outbound-secret", encrypted);
        Assert.Equal("outbound-secret", encryptor.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_UsesAFreshNonceEachTime()
    {
        var encryptor = Create();

        Assert.NotEqual(encryptor.Encrypt("same"), encryptor.Encrypt("same"));
    }

    [Fact]
    public void Decrypt_RejectsTamperedValues()
    {
        var encryptor = Create();
        var payload = Convert.FromBase64String(encryptor.Encrypt("value")["v1:".Length..]);
        payload[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt("v1:" + Convert.ToBase64String(payload)));
    }

    [Fact]
    public void Decrypt_RejectsAValueEncryptedWithAnotherKey()
    {
        var encrypted = Create().Encrypt("value");

        Assert.ThrowsAny<CryptographicException>(() => Create().Decrypt(encrypted));
    }

    [Theory]
    [InlineData("plain-text")]
    [InlineData("v1:not base64")]
    [InlineData("v1:AAAA")]
    public void Decrypt_RejectsMalformedValues(string value)
    {
        Assert.ThrowsAny<CryptographicException>(() => Create().Decrypt(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void Encrypt_ExplainsAnInvalidKey(string key)
    {
        var encryptor = new SecretEncryptor(Options.Create(new SecurityOptions { EncryptionKey = key }));

        var exception = Assert.Throws<InvalidOperationException>(() => encryptor.Encrypt("value"));
        Assert.Contains("Security:EncryptionKey", exception.Message);
    }
}
