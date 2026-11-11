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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

public sealed class SigningKeyProvider : ISigningKeyProvider
{
    private static readonly TimeSpan PublicKeyCacheLifetime = TimeSpan.FromMinutes(10);

    private readonly ISigningKeyRepository _keys;
    private readonly ISecretStore _secrets;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;

    public SigningKeyProvider(ISigningKeyRepository keys, ISecretStore secrets, IMemoryCache cache, TimeProvider timeProvider)
    {
        _keys = keys;
        _secrets = secrets;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public async Task<ActiveSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken)
    {
        var key = await _keys.GetActiveAsync(_timeProvider.GetUtcNow(), cancellationToken)
            ?? throw new SigningKeyUnavailableException("There is no active signing key. Rotate a key in the admin portal.");

        var privatePem = await _secrets.GetAsync(key.PrivateKeyRef, cancellationToken);
        if (string.IsNullOrEmpty(privatePem))
        {
            throw new SigningKeyUnavailableException($"The private key for signing key '{key.Kid}' is not in the secret store.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privatePem);
        return new ActiveSigningKey(key.Id, key.Kid, new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true)) { KeyId = key.Kid });
    }

    public async Task<SecurityKey?> GetVerificationKeyAsync(string kid, CancellationToken cancellationToken)
    {
        var cacheKey = $"signing-key-public:{kid}";
        if (_cache.TryGetValue(cacheKey, out SecurityKey? cached))
        {
            return cached;
        }

        var key = await _keys.GetByKidAsync(kid, cancellationToken);
        if (key is null)
        {
            return null;
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PublicKey);
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false)) { KeyId = key.Kid };
        _cache.Set(cacheKey, publicKey, PublicKeyCacheLifetime);
        return publicKey;
    }
}
