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

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

/// <summary>PBKDF2-HMAC-SHA256 with a random salt. The stored form is <c>pbkdf2-sha256$iterations$salt$hash</c>, both parts base64.</summary>
public sealed class ClientSecretHasher : IClientSecretHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const char Separator = '$';
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(secret, salt, Iterations);
        return string.Join(Separator, Scheme, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string secret, string hash)
    {
        var parts = hash.Split(Separator);
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            return expected.Length == HashSize && CryptographicOperations.FixedTimeEquals(Derive(secret, salt, iterations), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, HashSize);
    }
}
