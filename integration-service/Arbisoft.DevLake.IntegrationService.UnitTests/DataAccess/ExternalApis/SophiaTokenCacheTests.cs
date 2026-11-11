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

using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;
using Microsoft.Extensions.Time.Testing;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public sealed class SophiaTokenCacheTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly SophiaTokenCache _cache;
    private int _fetches;

    public SophiaTokenCacheTests()
    {
        _cache = new SophiaTokenCache(_time);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private Task<FetchedToken> Fetch(TimeSpan lifetime)
    {
        return Task.FromResult(new FetchedToken($"token-{Interlocked.Increment(ref _fetches)}", lifetime));
    }

    [Fact]
    public async Task ATokenIsFetchedOnce_ThenServedFromTheCache()
    {
        Assert.Equal("token-1", await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None));
        Assert.Equal("token-1", await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None));
        Assert.Equal(1, _fetches);
    }

    [Fact]
    public async Task EachKey_HasItsOwnToken()
    {
        var first = await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None);
        var second = await _cache.GetOrFetchAsync("b", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task ATokenExpiresASkewBeforeSophiaSaysItDoes()
    {
        await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromMinutes(10)), CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(10) - SophiaTokenCache.ExpirySkew - TimeSpan.FromSeconds(1));
        await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromMinutes(10)), CancellationToken.None);
        Assert.Equal(1, _fetches);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromMinutes(10)), CancellationToken.None);
        Assert.Equal(2, _fetches);
    }

    [Fact]
    public async Task Invalidate_ForgetsOnlyThatToken()
    {
        await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None);
        await _cache.GetOrFetchAsync("b", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None);

        _cache.Invalidate("a");
        await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None);
        await _cache.GetOrFetchAsync("b", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.Equal(3, _fetches);
    }

    [Fact]
    public async Task AFetchThatFails_LeavesNothingCached()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.GetOrFetchAsync("a", _ => Task.FromException<FetchedToken>(new InvalidOperationException("down")), CancellationToken.None));

        Assert.Equal("token-1", await _cache.GetOrFetchAsync("a", _ => Fetch(TimeSpan.FromHours(1)), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSameToken_ShareOneFetch()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _cache.GetOrFetchAsync(
            "a",
            async token =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), token);
                return await Fetch(TimeSpan.FromHours(1));
            },
            CancellationToken.None)));

        Assert.Single(results.Distinct());
        Assert.Equal(1, _fetches);
    }

    [Fact]
    public async Task AWaitingCaller_CanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var slow = _cache.GetOrFetchAsync("a", async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return await Fetch(TimeSpan.FromHours(1));
        }, cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);
    }
}
