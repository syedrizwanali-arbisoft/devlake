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
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

public sealed class SigningKeyProvider(
    ISigningKeyRepository repository,
    ISecretStore secretStore,
    TimeProvider timeProvider) : ISigningKeyProvider
{
    public async Task<ActiveSigningKey> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var key = await repository.GetActiveAsync(now, cancellationToken)
            ?? throw new InvalidOperationException("There is no active signing key.");
        EnsureSupported(key);

        var privateKeyPem = await secretStore.GetSecretAsync(key.PrivateKeyRef, cancellationToken);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new InvalidOperationException(
                $"The private key of signing key '{key.Kid}' was not found in the secret store under '{key.PrivateKeyRef}'.");
        }

        var credentials = new SigningCredentials(CreateRsaKey(privateKeyPem, key.Kid), key.Algorithm);
        return new ActiveSigningKey(key.Id, credentials);
    }

    public async Task<IReadOnlyList<SecurityKey>> GetVerificationKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = await repository.GetAllAsync(cancellationToken);
        var verificationKeys = new List<SecurityKey>(keys.Count);
        foreach (var key in keys)
        {
            EnsureSupported(key);
            verificationKeys.Add(CreateRsaKey(key.PublicKey, key.Kid));
        }

        return verificationKeys;
    }

    private static void EnsureSupported(SigningKey key)
    {
        if (key.Algorithm != SecurityAlgorithms.RsaSha256)
        {
            throw new InvalidOperationException($"Signing key '{key.Kid}' uses unsupported algorithm '{key.Algorithm}'.");
        }
    }

    // The security key keeps using the RSA instance, so it must not be disposed here.
    private static RsaSecurityKey CreateRsaKey(string pem, string kid)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa) { KeyId = kid };
    }
}
