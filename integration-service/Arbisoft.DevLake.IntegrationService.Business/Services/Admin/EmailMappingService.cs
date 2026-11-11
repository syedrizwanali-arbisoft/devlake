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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IEmailMappingService
{
    Task<PageDto<EmailMappingDto>> ListAsync(string? search, int page, CancellationToken cancellationToken);

    Task<EmailMappingDto?> GetAsync(long id, CancellationToken cancellationToken);

    Task<AdminResult<long>> CreateAsync(EmailMappingInput input, CancellationToken cancellationToken);

    Task<AdminResult> UpdateAsync(long id, EmailMappingInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Maps an engineer's Arbisoft email to the other emails they commit with. Both addresses are normalized by <see cref="EmailNormalizer"/> before
/// they are compared or stored, and the mapped address is also kept as typed.
/// </summary>
public sealed class EmailMappingService : IEmailMappingService
{
    public const int PageSize = 50;
    public const int MaxTypedLength = 255;

    private readonly IEmailMappingRepository _mappings;
    private readonly IOptions<RequestsOptions> _options;
    private readonly TimeProvider _timeProvider;

    public EmailMappingService(IEmailMappingRepository mappings, IOptions<RequestsOptions> options, TimeProvider timeProvider)
    {
        _mappings = mappings;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<PageDto<EmailMappingDto>> ListAsync(string? search, int page, CancellationToken cancellationToken)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : EmailNormalizer.Canonical(search);
        var result = await _mappings.ListAsync(term, page, PageSize, cancellationToken);
        return new PageDto<EmailMappingDto>(result.Items.Select(ToDto).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<EmailMappingDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var mapping = await _mappings.GetByIdAsync(id, cancellationToken);
        return mapping is null ? null : ToDto(mapping);
    }

    public async Task<AdminResult<long>> CreateAsync(EmailMappingInput input, CancellationToken cancellationToken)
    {
        var checkedInput = await CheckAsync(input, exceptId: null, cancellationToken);
        if (checkedInput.Error is not null)
        {
            return AdminResult.Failure<long>(checkedInput.Error);
        }

        var id = await _mappings.InsertAsync(
            new EmailMapping
            {
                ArbisoftEmail = checkedInput.ArbisoftEmail,
                Email = checkedInput.Email,
                EmailAsReceived = input.Email,
                CreatedOn = _timeProvider.GetUtcNow()
            },
            cancellationToken);
        return AdminResult.Success(id);
    }

    public async Task<AdminResult> UpdateAsync(long id, EmailMappingInput input, CancellationToken cancellationToken)
    {
        var existing = await _mappings.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return AdminResult.Fail("The mapping does not exist.");
        }

        var checkedInput = await CheckAsync(input, id, cancellationToken);
        if (checkedInput.Error is not null)
        {
            return AdminResult.Fail(checkedInput.Error);
        }

        await _mappings.UpdateAsync(
            new EmailMapping { Id = id, ArbisoftEmail = checkedInput.ArbisoftEmail, Email = checkedInput.Email, EmailAsReceived = input.Email, CreatedOn = existing.CreatedOn },
            cancellationToken);
        return AdminResult.Ok;
    }

    private async Task<(string ArbisoftEmail, string Email, string? Error)> CheckAsync(EmailMappingInput input, long? exceptId, CancellationToken cancellationToken)
    {
        if (!EmailNormalizer.TryNormalize(input.ArbisoftEmail, _options.Value.AllowedEmailDomains, out var arbisoft, out var arbisoftError))
        {
            return (string.Empty, string.Empty, $"Arbisoft email: {arbisoftError}");
        }

        if ((input.Email ?? string.Empty).Length > MaxTypedLength)
        {
            return (string.Empty, string.Empty, "Mapped email: The email is not a valid email address.");
        }

        if (!EmailNormalizer.TryNormalizeAnyDomain(input.Email, out var other, out var otherError))
        {
            return (string.Empty, string.Empty, $"Mapped email: {otherError}");
        }

        if (string.Equals(arbisoft, other, StringComparison.OrdinalIgnoreCase))
        {
            return (string.Empty, string.Empty, "The mapped email must differ from the Arbisoft email.");
        }

        return await _mappings.ExistsAsync(arbisoft, other, exceptId, cancellationToken)
            ? (string.Empty, string.Empty, "This mapping already exists.")
            : (arbisoft, other, null);
    }

    private static EmailMappingDto ToDto(EmailMapping m) => new(m.Id, m.ArbisoftEmail, m.Email, m.EmailAsReceived, m.CreatedOn);
}
