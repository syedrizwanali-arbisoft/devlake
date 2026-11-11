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

using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IScopeService
{
    Task<IReadOnlyList<ScopeDto>> ListAsync(CancellationToken cancellationToken);

    Task<ScopeDto?> GetAsync(long id, CancellationToken cancellationToken);

    Task<AdminResult<long>> CreateAsync(string name, string description, CancellationToken cancellationToken);

    Task<AdminResult> UpdateAsync(long id, string name, string description, CancellationToken cancellationToken);
}

/// <summary>Scopes are names such as <c>stats:read</c> that the APIs require. Clients are granted scopes in the client details page.</summary>
public sealed partial class ScopeService : IScopeService
{
    public const int MaxDescriptionLength = 500;

    private readonly IScopeRepository _scopes;
    private readonly TimeProvider _timeProvider;

    public ScopeService(IScopeRepository scopes, TimeProvider timeProvider)
    {
        _scopes = scopes;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ScopeDto>> ListAsync(CancellationToken cancellationToken)
    {
        var page = await _scopes.ListAsync(1, AdminPaging.MaxRows, cancellationToken);
        return page.Items.Select(ToDto).ToList();
    }

    public async Task<ScopeDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var scope = await _scopes.GetByIdAsync(id, cancellationToken);
        return scope is null ? null : ToDto(scope);
    }

    public async Task<AdminResult<long>> CreateAsync(string name, string description, CancellationToken cancellationToken)
    {
        var problem = Validate(name, description);
        if (problem is not null)
        {
            return AdminResult.Failure<long>(problem);
        }

        if (await _scopes.GetByNameAsync(name, cancellationToken) is not null)
        {
            return AdminResult.Failure<long>($"A scope named '{name}' already exists.");
        }

        var id = await _scopes.InsertAsync(new Scope { Name = name, Description = description.Trim(), CreatedOn = _timeProvider.GetUtcNow() }, cancellationToken);
        return AdminResult.Success(id);
    }

    public async Task<AdminResult> UpdateAsync(long id, string name, string description, CancellationToken cancellationToken)
    {
        var problem = Validate(name, description);
        if (problem is not null)
        {
            return AdminResult.Fail(problem);
        }

        var existing = await _scopes.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return AdminResult.Fail("The scope does not exist.");
        }

        var sameName = await _scopes.GetByNameAsync(name, cancellationToken);
        if (sameName is not null && sameName.Id != id)
        {
            return AdminResult.Fail($"A scope named '{name}' already exists.");
        }

        await _scopes.UpdateAsync(new Scope { Id = id, Name = name, Description = description.Trim(), CreatedOn = existing.CreatedOn }, cancellationToken);
        return AdminResult.Ok;
    }

    private static string? Validate(string name, string description)
    {
        if (!NamePattern().IsMatch(name ?? string.Empty))
        {
            return "The name must be 2 to 128 characters: lowercase letters, digits and . _ : -, starting with a letter.";
        }

        return (description ?? string.Empty).Length > MaxDescriptionLength ? $"The description can be at most {MaxDescriptionLength} characters." : null;
    }

    private static ScopeDto ToDto(Scope scope) => new(scope.Id, scope.Name, scope.Description, scope.CreatedOn);

    [GeneratedRegex("^[a-z][a-z0-9._:-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
