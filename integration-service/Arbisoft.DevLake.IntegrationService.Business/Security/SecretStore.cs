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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Configuration;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

/// <summary>
/// Looks a reference up in configuration first, normally the Secrets__{reference} environment variable, and then in
/// the SecretStoreEntries table, where values are encrypted with Security:EncryptionKey. New secrets go to the table.
/// </summary>
public sealed class SecretStore(
    IConfiguration configuration,
    ISecretStoreRepository repository,
    ISecretEncryptor encryptor,
    TimeProvider timeProvider) : ISecretStore
{
    public async Task<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default)
    {
        var configured = configuration[$"Secrets:{reference}"];
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var entry = await repository.GetAsync(reference, cancellationToken);
        return entry is null ? null : encryptor.Decrypt(entry.ValueEncrypted);
    }

    public async Task SaveSecretAsync(string reference, string value, string createdBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrEmpty(value);

        await repository.AddAsync(new SecretStoreEntry
        {
            SecretRef = reference,
            ValueEncrypted = encryptor.Encrypt(value),
            CreatedOn = timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = createdBy,
        }, cancellationToken);
    }
}
