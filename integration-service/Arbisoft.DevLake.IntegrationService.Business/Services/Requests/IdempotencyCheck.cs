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
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class IdempotencyCheck(
    IRequestLogRepository requestLogs,
    IOptions<RequestOptions> options,
    TimeProvider timeProvider) : IIdempotencyCheck
{
    /// <summary>Email order doesn't matter, so the same batch in another order is still a duplicate.</summary>
    public string ComputeHash(string eventType, IReadOnlyList<string> normalizedEmails, TimeFrame timeFrame)
    {
        var emails = normalizedEmails.Order(StringComparer.Ordinal);
        var key = string.Join('\n', eventType, timeFrame.ToString(), string.Join(',', emails));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    public async Task<string?> FindDuplicateAsync(long clientRefId, string requestHash, CancellationToken cancellationToken = default)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-options.Value.DuplicateWindowSeconds);
        var duplicate = await requestLogs.FindRecentDuplicateAsync(clientRefId, requestHash, since, cancellationToken);
        return duplicate?.CorrelationId;
    }
}
