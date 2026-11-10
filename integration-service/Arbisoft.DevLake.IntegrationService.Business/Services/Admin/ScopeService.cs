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
    Task<IReadOnlyList<ScopeDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ScopeDto?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminResult<long>> CreateAsync(string name, string description, CancellationToken cancellationToken = default);
    Task<AdminResult> UpdateAsync(long id, string name, string description, CancellationToken cancellationToken = default);
}

public sealed partial class ScopeService(IScopeRepository scopes, TimeProvider timeProvider) : IScopeService
{
    public async Task<IReadOnlyList<ScopeDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await scopes.GetAllAsync(cancellationToken)).Select(ToDto).ToList();

    public async Task<ScopeDto?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        await scopes.GetByIdAsync(id, cancellationToken) is { } scope ? ToDto(scope) : null;

    public async Task<AdminResult<long>> CreateAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        var error = await ValidateAsync(null, name, description, cancellationToken);
        if (error is not null)
        {
            return AdminResult<long>.Fail(error);
        }

        var id = await scopes.AddAsync(new Scope
        {
            Name = name.Trim(), Description = description.Trim(), CreatedOn = timeProvider.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        return AdminResult<long>.Ok(id);
    }

    /// <remarks>Renaming a scope changes what new tokens carry, and the APIs check scopes by name.</remarks>
    public async Task<AdminResult> UpdateAsync(long id, string name, string description, CancellationToken cancellationToken = default)
    {
        var scope = await scopes.GetByIdAsync(id, cancellationToken);
        if (scope is null)
        {
            return AdminResult.Fail("The scope was not found.");
        }

        var error = await ValidateAsync(id, name, description, cancellationToken);
        if (error is not null)
        {
            return AdminResult.Fail(error);
        }

        scope.Name = name.Trim();
        scope.Description = description.Trim();
        await scopes.UpdateAsync(scope, cancellationToken);
        return AdminResult.Ok;
    }

    private async Task<string?> ValidateAsync(long? id, string name, string description, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (!NamePattern().IsMatch(trimmed))
        {
            return "The name must be 2 to 128 characters, start with a lowercase letter, and use only lowercase letters, digits, '.', ':', '_' or '-'.";
        }

        if ((description ?? string.Empty).Trim().Length > 1024)
        {
            return "The description can be at most 1024 characters.";
        }

        var existing = (await scopes.GetAllAsync(cancellationToken)).FirstOrDefault(s => s.Name == trimmed);
        return existing is not null && existing.Id != id ? $"A scope named '{trimmed}' already exists." : null;
    }

    private static ScopeDto ToDto(Scope s) => new(s.Id, s.Name, s.Description, s.CreatedOn);

    [GeneratedRegex("^[a-z][a-z0-9._:-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
