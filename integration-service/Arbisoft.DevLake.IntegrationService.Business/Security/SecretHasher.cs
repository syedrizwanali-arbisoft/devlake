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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

/// <summary>Salted PBKDF2-SHA256, stored as "pbkdf2-sha256$iterations$salt$hash".</summary>
public sealed class SecretHasher : ISecretHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(secret, salt, Iterations, HashSize);
        return string.Join('$', Algorithm, Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Returns false for a malformed stored hash, so one bad row can't turn a login into a server error.</summary>
    public bool Verify(string secret, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (expected.Length == 0)
            {
                return false;
            }

            var actual = Derive(secret, salt, iterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations, int size) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, size);
}
