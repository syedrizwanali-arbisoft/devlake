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
using System.Text;
using System.Text.Json;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>
/// Decides whether a request repeats an earlier one. The database's unique key on the idempotency key is what actually prevents a duplicate;
/// this class applies the window rules around it: a request outside the duplicate window, or one that failed, does not block a repeat, and a
/// revoked request never held a key to begin with.
/// </summary>
public sealed class IdempotencyCheck : IIdempotencyCheck
{
    private readonly IRequestLogRepository _requestLogs;
    private readonly IOptionsSnapshot<RequestsOptions> _options;
    private readonly TimeProvider _timeProvider;

    public IdempotencyCheck(IRequestLogRepository requestLogs, IOptionsSnapshot<RequestsOptions> options, TimeProvider timeProvider)
    {
        _requestLogs = requestLogs;
        _options = options;
        _timeProvider = timeProvider;
    }

    public string ComputeKey(long clientRefId, string eventType, StatsPeriod period, IReadOnlyCollection<string> normalizedEmails)
    {
        // A JSON array cannot be read two ways: no choice of values can make two different requests produce the same text.
        var canonical = JsonSerializer.Serialize(new object[] { clientRefId, eventType, period.Canonical, normalizedEmails.Order(StringComparer.Ordinal).ToArray() });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public async Task<RequestLog?> FindDuplicateAsync(string key, CancellationToken cancellationToken)
    {
        var existing = await _requestLogs.GetByIdempotencyKeyAsync(key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var windowStart = _timeProvider.GetUtcNow().AddMinutes(-_options.Value.DuplicateWindowMinutes);
        if (existing.CreatedOn < windowStart || existing.Status == RequestStatus.Failed)
        {
            await _requestLogs.ReleaseIdempotencyKeyAsync(existing.Id, cancellationToken);
            return null;
        }

        return existing;
    }
}
