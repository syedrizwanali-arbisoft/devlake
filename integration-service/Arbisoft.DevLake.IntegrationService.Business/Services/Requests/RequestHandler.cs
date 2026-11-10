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

using System.Net.Mail;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>Validates a stats request, answers duplicates with the original acknowledgement, and passes new ones on.</summary>
public sealed class RequestHandler(
    IClientRepository clients,
    IIdempotencyCheck idempotencyCheck,
    IRequestService requestService,
    IOptions<RequestOptions> options) : IRequestHandler
{
    public Task<StatsSubmissionResult> SubmitSingleAsync(
        string clientId, string? email, string? timeFrame, string url, CancellationToken cancellationToken = default)
    {
        return SubmitAsync(clientId, RequestEventTypes.EngineerStats, [email ?? string.Empty], timeFrame, url, cancellationToken);
    }

    public Task<StatsSubmissionResult> SubmitBatchAsync(
        string clientId, IReadOnlyList<string>? emails, string? timeFrame, string url, CancellationToken cancellationToken = default)
    {
        var max = options.Value.MaxBatchEmails;
        if (emails is null || emails.Count == 0)
        {
            return Task.FromResult(StatsSubmissionResult.Invalid("At least one email is required."));
        }

        if (emails.Count > max)
        {
            return Task.FromResult(StatsSubmissionResult.Invalid($"A batch can hold at most {max} emails."));
        }

        return SubmitAsync(clientId, RequestEventTypes.EngineerStatsBatch, emails, timeFrame, url, cancellationToken);
    }

    private async Task<StatsSubmissionResult> SubmitAsync(
        string clientId,
        string eventType,
        IReadOnlyList<string> emails,
        string? timeFrameValue,
        string url,
        CancellationToken cancellationToken)
    {
        if (!TimeFrame.TryParse(timeFrameValue, out var timeFrame))
        {
            return StatsSubmissionResult.Invalid($"Time frame '{timeFrameValue}' is not valid. Use {TimeFrame.FormatDescription}.");
        }

        var normalized = new List<string>(emails.Count);
        var invalid = new List<string>();
        foreach (var email in emails)
        {
            if (TryNormalize(email, out var value))
            {
                normalized.Add(value);
            }
            else
            {
                invalid.Add(email ?? string.Empty);
            }
        }

        if (invalid.Count > 0)
        {
            var domain = options.Value.AllowedEmailDomain;
            return StatsSubmissionResult.Invalid(
                $"Not valid @{domain} email(s): {string.Join(", ", invalid.Select(e => $"'{e}'"))}.");
        }

        var client = await clients.GetByClientIdAsync(clientId, cancellationToken);
        if (client is not { IsActive: true })
        {
            return new StatsSubmissionResult(StatsSubmissionOutcome.UnknownClient, string.Empty, "The client is not registered or not active.");
        }

        var distinctEmails = normalized.Distinct(StringComparer.Ordinal).ToList();
        var hash = idempotencyCheck.ComputeHash(eventType, distinctEmails, timeFrame);
        var duplicateOf = await idempotencyCheck.FindDuplicateAsync(client.Id, hash, cancellationToken);
        if (duplicateOf is not null)
        {
            return new StatsSubmissionResult(StatsSubmissionOutcome.Duplicate, duplicateOf, string.Empty);
        }

        return await requestService.QueueAsync(
            new StatsSubmission(client.Id, client.ClientId, eventType, distinctEmails, timeFrame, hash, url), cancellationToken);
    }

    private bool TryNormalize(string? email, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !MailAddress.TryCreate(trimmed, out var address)
            || address.Address != trimmed
            || !string.IsNullOrEmpty(address.DisplayName)
            || !string.Equals(address.Host, options.Value.AllowedEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = trimmed.ToLowerInvariant();
        return true;
    }
}
