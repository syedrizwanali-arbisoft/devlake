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
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IEmailMappingService
{
    /// <summary>Lists mappings, optionally only those whose Arbisoft or mapped email contains <paramref name="search"/>.</summary>
    Task<IReadOnlyList<EmailMappingDto>> ListAsync(string? search = null, CancellationToken cancellationToken = default);
    Task<EmailMappingDto?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminResult<long>> CreateAsync(string arbisoftEmail, string email, CancellationToken cancellationToken = default);
    Task<AdminResult> UpdateAsync(long id, string arbisoftEmail, string email, CancellationToken cancellationToken = default);
}

public sealed class EmailMappingService(
    IEmailMappingRepository mappings,
    IOptions<RequestOptions> requestOptions,
    TimeProvider timeProvider) : IEmailMappingService
{
    public async Task<IReadOnlyList<EmailMappingDto>> ListAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var term = search?.Trim().ToLowerInvariant();
        return (await mappings.GetAllAsync(cancellationToken))
            .Where(m => string.IsNullOrEmpty(term) || m.ArbisoftEmail.Contains(term, StringComparison.OrdinalIgnoreCase)
                || m.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(ToDto)
            .ToList();
    }

    public async Task<EmailMappingDto?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        await mappings.GetByIdAsync(id, cancellationToken) is { } mapping ? ToDto(mapping) : null;

    public async Task<AdminResult<long>> CreateAsync(string arbisoftEmail, string email, CancellationToken cancellationToken = default)
    {
        var (normalized, error) = await ValidateAsync(null, arbisoftEmail, email, cancellationToken);
        if (error is not null)
        {
            return AdminResult<long>.Fail(error);
        }

        var id = await mappings.AddAsync(new EmailMapping
        {
            ArbisoftEmail = normalized.Arbisoft, Email = normalized.Email, CreatedOn = timeProvider.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        return AdminResult<long>.Ok(id);
    }

    public async Task<AdminResult> UpdateAsync(long id, string arbisoftEmail, string email, CancellationToken cancellationToken = default)
    {
        var mapping = await mappings.GetByIdAsync(id, cancellationToken);
        if (mapping is null)
        {
            return AdminResult.Fail("The mapping was not found.");
        }

        var (normalized, error) = await ValidateAsync(id, arbisoftEmail, email, cancellationToken);
        if (error is not null)
        {
            return AdminResult.Fail(error);
        }

        mapping.ArbisoftEmail = normalized.Arbisoft;
        mapping.Email = normalized.Email;
        await mappings.UpdateAsync(mapping, cancellationToken);
        return AdminResult.Ok;
    }

    private async Task<((string Arbisoft, string Email) Normalized, string? Error)> ValidateAsync(
        long? id, string arbisoftEmail, string email, CancellationToken cancellationToken)
    {
        var domain = requestOptions.Value.AllowedEmailDomain;
        var arbisoft = Normalize(arbisoftEmail);
        var mapped = Normalize(email);
        if (arbisoft is null || !arbisoft.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase))
        {
            return (default, $"The Arbisoft email must be a valid @{domain} address.");
        }

        if (mapped is null)
        {
            return (default, "The mapped email must be a valid email address.");
        }

        if (mapped == arbisoft)
        {
            return (default, "An Arbisoft email is always included; it doesn't need a mapping to itself.");
        }

        var duplicate = (await mappings.GetAllAsync(cancellationToken))
            .Any(m => m.Id != id && m.ArbisoftEmail == arbisoft && m.Email == mapped);
        return duplicate ? (default, $"{arbisoft} is already mapped to {mapped}.") : ((arbisoft, mapped), null);
    }

    private static string? Normalize(string? email)
    {
        var trimmed = email?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= 320 && MailAddress.TryCreate(trimmed, out var address)
            && address.Address == trimmed && string.IsNullOrEmpty(address.DisplayName)
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static EmailMappingDto ToDto(EmailMapping m) => new(m.Id, m.ArbisoftEmail, m.Email, m.CreatedOn);
}
