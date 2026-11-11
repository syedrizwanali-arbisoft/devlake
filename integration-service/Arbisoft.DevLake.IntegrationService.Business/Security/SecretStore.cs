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

using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Configuration;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

public sealed class SecretStore : ISecretStore
{
    private const string OverrideSection = "Secrets";

    private readonly ISecretStoreRepository _repository;
    private readonly ISecretEncryptor _encryptor;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public SecretStore(ISecretStoreRepository repository, ISecretEncryptor encryptor, IConfiguration configuration, TimeProvider timeProvider)
    {
        _repository = repository;
        _encryptor = encryptor;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public async Task<string?> GetAsync(string secretRef, CancellationToken cancellationToken)
    {
        var overridden = _configuration[$"{OverrideSection}:{secretRef}"];
        if (!string.IsNullOrEmpty(overridden))
        {
            return overridden;
        }

        var stored = await _repository.GetEncryptedValueAsync(secretRef, cancellationToken);
        return stored is null ? null : _encryptor.Decrypt(stored);
    }

    public Task SaveAsync(string secretRef, string value, CancellationToken cancellationToken)
    {
        return _repository.SaveEncryptedValueAsync(secretRef, _encryptor.Encrypt(value), _timeProvider.GetUtcNow(), cancellationToken);
    }
}
