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

using System.Collections.Concurrent;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;

/// <summary>An access token and how long Sophia said it lasts.</summary>
public sealed record FetchedToken(string Value, TimeSpan Lifetime);

/// <summary>
/// Keeps Sophia's access tokens until shortly before they expire, so a run of callbacks does not ask for a token each time. Only one fetch runs at
/// a time, so a burst of callbacks that all find the cache empty makes one token request, not one each.
/// </summary>
public sealed class SophiaTokenCache : IDisposable
{
    /// <summary>A token is treated as expired this long before Sophia's expiry, so it is never sent just as it lapses.</summary>
    public static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new();
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly TimeProvider _timeProvider;

    public SophiaTokenCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<string> GetOrFetchAsync(string key, Func<CancellationToken, Task<FetchedToken>> fetch, CancellationToken cancellationToken)
    {
        if (TryGetFresh(key, out var cached))
        {
            return cached;
        }

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh(key, out cached))
            {
                return cached;
            }

            var fetched = await fetch(cancellationToken);
            var usableFor = fetched.Lifetime - ExpirySkew;
            if (usableFor > TimeSpan.Zero)
            {
                _tokens[key] = new CachedToken(fetched.Value, _timeProvider.GetUtcNow() + usableFor);
            }

            return fetched.Value;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>Forgets a token, for example after Sophia rejected it, so the next call fetches a new one.</summary>
    public void Invalidate(string key)
    {
        _tokens.TryRemove(key, out _);
    }

    public void Dispose()
    {
        _fetchLock.Dispose();
    }

    private bool TryGetFresh(string key, out string token)
    {
        if (_tokens.TryGetValue(key, out var cached) && cached.UsableUntil > _timeProvider.GetUtcNow())
        {
            token = cached.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private sealed record CachedToken(string Value, DateTimeOffset UsableUntil);
}
