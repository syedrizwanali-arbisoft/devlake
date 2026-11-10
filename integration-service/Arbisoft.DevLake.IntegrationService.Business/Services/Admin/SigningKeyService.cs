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
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface ISigningKeyService
{
    Task<IReadOnlyList<SigningKeyDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new RSA key, stores its private key encrypted in the secret store, makes it the active key and retires
    /// the previous one. Tokens signed with the old key keep verifying until they expire.
    /// </summary>
    Task<AdminResult<SigningKeyDto>> RotateAsync(string rotatedBy, CancellationToken cancellationToken = default);
}

public sealed class SigningKeyService(
    ISigningKeyRepository signingKeys,
    ISecretStore secretStore,
    TimeProvider timeProvider) : ISigningKeyService
{
    private const int KeySizeBits = 2048;

    public async Task<IReadOnlyList<SigningKeyDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = Now();
        var active = await signingKeys.GetActiveAsync(now, cancellationToken);
        return (await signingKeys.GetAllAsync(cancellationToken)).Select(k => ToDto(k, active?.Id, now)).ToList();
    }

    public async Task<AdminResult<SigningKeyDto>> RotateAsync(string rotatedBy, CancellationToken cancellationToken = default)
    {
        var now = Now();
        var previous = await signingKeys.GetActiveAsync(now, cancellationToken);

        using var rsa = RSA.Create(KeySizeBits);
        var kid = "key-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        var privateKeyRef = "signing-key-" + kid;

        // The private key is stored before the key row exists, so an active key always has its private key.
        await secretStore.SaveSecretAsync(privateKeyRef, rsa.ExportPkcs8PrivateKeyPem(), rotatedBy, cancellationToken);
        var key = new SigningKey
        {
            Kid = kid,
            Algorithm = SecurityAlgorithms.RsaSha256,
            PublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            PrivateKeyRef = privateKeyRef,
            ActivatedOn = now,
            CreatedOn = now,
        };
        key.Id = await signingKeys.AddAsync(key, cancellationToken);

        if (previous is not null)
        {
            await signingKeys.RetireAsync(previous.Id, now, cancellationToken);
        }

        return AdminResult<SigningKeyDto>.Ok(ToDto(key, key.Id, now));
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static SigningKeyDto ToDto(SigningKey k, long? activeId, DateTime now) => new(
        k.Id, k.Kid, k.Algorithm, k.PrivateKeyRef, k.ActivatedOn, k.RetiredOn, k.CreatedOn,
        k.Id == activeId ? "Active" : k.ActivatedOn > now ? "Pending" : "Retired");
}
