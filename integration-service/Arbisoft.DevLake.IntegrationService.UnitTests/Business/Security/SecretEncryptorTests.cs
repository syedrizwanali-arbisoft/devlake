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
using Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class SecretEncryptorTests
{
    private readonly SecretEncryptor _encryptor = new(SecurityTestData.Options());

    [Fact]
    public void Decrypt_ReturnsWhatWasEncrypted_IncludingUnicodeAndMultilineText()
    {
        const string secret = "-----BEGIN PRIVATE KEY-----\nabc é 日本\n-----END PRIVATE KEY-----";

        Assert.Equal(secret, _encryptor.Decrypt(_encryptor.Encrypt(secret)));
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_GivesDifferentCiphertexts()
    {
        Assert.NotEqual(_encryptor.Encrypt("same"), _encryptor.Encrypt("same"));
    }

    [Fact]
    public void Encrypt_DoesNotContainThePlaintext_AndIsVersioned()
    {
        var protectedValue = _encryptor.Encrypt("plain-secret-value");

        Assert.StartsWith("v1.", protectedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-secret-value", protectedValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Decrypt_AlteredValue_Throws()
    {
        var bytes = Convert.FromBase64String(_encryptor.Encrypt("secret")[3..]);
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.Decrypt("v1." + Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Decrypt_WithAnotherMasterKey_Throws()
    {
        var other = new SecretEncryptor(SecurityTestData.Options());

        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(_encryptor.Encrypt("secret")));
    }

    [Theory]
    [InlineData("not-versioned")]
    [InlineData("v1.AAAA")]
    public void Decrypt_UnsupportedOrTruncatedValue_Throws(string value)
    {
        Assert.Throws<CryptographicException>(() => _encryptor.Decrypt(value));
    }
}
