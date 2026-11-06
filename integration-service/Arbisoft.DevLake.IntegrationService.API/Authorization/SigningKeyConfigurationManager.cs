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

using Arbisoft.DevLake.IntegrationService.Business.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Arbisoft.DevLake.IntegrationService.API.Authorization;

/// <summary>
/// Gives JWT validation the verification keys from the database, cached briefly. A token with an unknown kid makes
/// JwtBearer call RequestRefresh, so a newly rotated key is picked up by the next request.
/// </summary>
public sealed class SigningKeyConfigurationManager(
    ISigningKeyProvider signingKeys,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IConfigurationManager<OpenIdConnectConfiguration>, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile OpenIdConnectConfiguration? _configuration;
    private long _expiresAtTicks;

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        var current = _configuration;
        if (current is not null && !IsExpired())
        {
            return current;
        }

        await _refreshLock.WaitAsync(cancel);
        try
        {
            if (_configuration is not null && !IsExpired())
            {
                return _configuration;
            }

            var configuration = new OpenIdConnectConfiguration { Issuer = jwtOptions.Value.Issuer };
            foreach (var key in await signingKeys.GetVerificationKeysAsync(cancel))
            {
                configuration.SigningKeys.Add(key);
            }

            _configuration = configuration;
            Interlocked.Exchange(ref _expiresAtTicks, timeProvider.GetUtcNow().Add(CacheDuration).UtcTicks);
            return configuration;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void RequestRefresh() => Interlocked.Exchange(ref _expiresAtTicks, 0);

    public void Dispose() => _refreshLock.Dispose();

    private bool IsExpired() => timeProvider.GetUtcNow().UtcTicks >= Interlocked.Read(ref _expiresAtTicks);
}
