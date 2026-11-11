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

using Arbisoft.DevLake.IntegrationService.DataAccess.Options;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.DevLake;

/// <summary>
/// Limits how many requests are in flight to DevLake across the whole process, however many batches are being processed. The rate limiter at the
/// edge protects this service; this gate protects DevLake (ASY-7).
/// </summary>
public sealed class DevLakeConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _slots;

    public DevLakeConcurrencyGate(IOptions<DevLakeOptions> options)
    {
        _slots = new SemaphoreSlim(options.Value.MaxConcurrentRequests, options.Value.MaxConcurrentRequests);
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            return await call();
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose()
    {
        _slots.Dispose();
    }
}
