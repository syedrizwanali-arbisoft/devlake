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
using System.Text.RegularExpressions;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ClientDetailsDto?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminResult<long>> CreateAsync(ClientInput input, string createdBy, CancellationToken cancellationToken = default);

    /// <summary>Updates the name, active flag and token lifetime. The Client ID never changes.</summary>
    Task<AdminResult> UpdateAsync(long id, ClientInput input, string modifiedBy, CancellationToken cancellationToken = default);

    Task<AdminResult<NewClientSecretDto>> CreateSecretAsync(long id, int validForDays, string createdBy, CancellationToken cancellationToken = default);
    Task<AdminResult> RevokeSecretAsync(long id, long secretId, CancellationToken cancellationToken = default);
    Task<AdminResult> SetScopesAsync(long id, IReadOnlyCollection<long> scopeIds, string changedBy, CancellationToken cancellationToken = default);
}

public sealed partial class ClientService(
    IClientRepository clients,
    IScopeRepository scopes,
    ISecretHasher secretHasher,
    TimeProvider timeProvider) : IClientService
{
    public const int MinLifetimeSeconds = 60;
    public const int MaxLifetimeSeconds = 86_400;
    public const int MaxSecretDays = 730;

    public async Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await clients.GetAllAsync(cancellationToken)).Select(ToDto).ToList();

    public async Task<ClientDetailsDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var client = await clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return null;
        }

        var now = Now();
        var secrets = (await clients.GetSecretsAsync(id, cancellationToken)).Select(s => ToDto(s, now)).ToList();
        var allScopes = (await scopes.GetAllAsync(cancellationToken))
            .Select(s => new ScopeDto(s.Id, s.Name, s.Description, s.CreatedOn)).ToList();
        var assignedNames = (await scopes.GetNamesForClientAsync(id, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return new ClientDetailsDto(ToDto(client), secrets, allScopes.Where(s => assignedNames.Contains(s.Name)).ToList(), allScopes);
    }

    public async Task<AdminResult<long>> CreateAsync(ClientInput input, string createdBy, CancellationToken cancellationToken = default)
    {
        var clientId = input.ClientId?.Trim() ?? string.Empty;
        if (!ClientIdPattern().IsMatch(clientId))
        {
            return AdminResult<long>.Fail("The Client ID must be 3 to 128 characters: lowercase letters, digits, dots, dashes or underscores.");
        }

        if (Validate(input) is { } error)
        {
            return AdminResult<long>.Fail(error);
        }

        if (await clients.GetByClientIdAsync(clientId, cancellationToken) is not null)
        {
            return AdminResult<long>.Fail($"A client with Client ID '{clientId}' already exists.");
        }

        var id = await clients.AddAsync(new Client
        {
            ClientId = clientId,
            Name = input.Name.Trim(),
            IsActive = input.IsActive,
            AccessTokenLifetimeSeconds = input.AccessTokenLifetimeSeconds,
            CreatedOn = Now(),
            CreatedBy = createdBy,
        }, cancellationToken);
        return AdminResult<long>.Ok(id);
    }

    public async Task<AdminResult> UpdateAsync(long id, ClientInput input, string modifiedBy, CancellationToken cancellationToken = default)
    {
        if (Validate(input) is { } error)
        {
            return AdminResult.Fail(error);
        }

        var client = await clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return AdminResult.Fail("The client was not found.");
        }

        client.Name = input.Name.Trim();
        client.IsActive = input.IsActive;
        client.AccessTokenLifetimeSeconds = input.AccessTokenLifetimeSeconds;
        client.ModifiedOn = Now();
        client.ModifiedBy = modifiedBy;
        await clients.UpdateAsync(client, cancellationToken);
        return AdminResult.Ok;
    }

    public async Task<AdminResult<NewClientSecretDto>> CreateSecretAsync(
        long id, int validForDays, string createdBy, CancellationToken cancellationToken = default)
    {
        if (validForDays is < 1 or > MaxSecretDays)
        {
            return AdminResult<NewClientSecretDto>.Fail($"A secret can be valid for 1 to {MaxSecretDays} days.");
        }

        var client = await clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return AdminResult<NewClientSecretDto>.Fail("The client was not found.");
        }

        // 32 random bytes as base64url: no colon, so it is safe in an HTTP Basic header.
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = Now();
        var expiresOn = now.AddDays(validForDays);
        var secretId = await clients.AddSecretAsync(new ClientSecret
        {
            ClientRefId = id,
            Secret = secretHasher.Hash(secret),
            ExpiresOn = expiresOn,
            CreatedOn = now,
            CreatedBy = createdBy,
        }, cancellationToken);
        return AdminResult<NewClientSecretDto>.Ok(new NewClientSecretDto(secretId, client.ClientId, secret, expiresOn));
    }

    public async Task<AdminResult> RevokeSecretAsync(long id, long secretId, CancellationToken cancellationToken = default)
    {
        var secrets = await clients.GetSecretsAsync(id, cancellationToken);
        var secret = secrets.FirstOrDefault(s => s.Id == secretId);
        if (secret is null)
        {
            return AdminResult.Fail("The secret was not found for this client.");
        }

        if (secret.RevokedOn is not null)
        {
            return AdminResult.Fail("The secret is already revoked.");
        }

        await clients.RevokeSecretAsync(secretId, Now(), cancellationToken);
        return AdminResult.Ok;
    }

    public async Task<AdminResult> SetScopesAsync(
        long id, IReadOnlyCollection<long> scopeIds, string changedBy, CancellationToken cancellationToken = default)
    {
        if (await clients.GetByIdAsync(id, cancellationToken) is null)
        {
            return AdminResult.Fail("The client was not found.");
        }

        var allScopes = await scopes.GetAllAsync(cancellationToken);
        var wanted = scopeIds.ToHashSet();
        if (wanted.Except(allScopes.Select(s => s.Id)).Any())
        {
            return AdminResult.Fail("One of the scopes doesn't exist.");
        }

        var current = (await scopes.GetNamesForClientAsync(id, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var now = Now();
        foreach (var scope in allScopes)
        {
            var assigned = current.Contains(scope.Name);
            if (wanted.Contains(scope.Id) && !assigned)
            {
                await scopes.AssignToClientAsync(new ClientScope
                {
                    ClientRefId = id, ScopeId = scope.Id, CreatedOn = now, CreatedBy = changedBy,
                }, cancellationToken);
            }
            else if (!wanted.Contains(scope.Id) && assigned)
            {
                await scopes.RemoveFromClientAsync(id, scope.Id, cancellationToken);
            }
        }

        return AdminResult.Ok;
    }

    private static string? Validate(ClientInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 255)
        {
            return "The name is required and can be at most 255 characters.";
        }

        return input.AccessTokenLifetimeSeconds is < MinLifetimeSeconds or > MaxLifetimeSeconds
            ? $"The token lifetime must be {MinLifetimeSeconds} to {MaxLifetimeSeconds} seconds."
            : null;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static ClientDto ToDto(Client c) => new(
        c.Id, c.ClientId, c.Name, c.IsActive, c.AccessTokenLifetimeSeconds, c.CreatedOn, c.CreatedBy, c.ModifiedOn, c.ModifiedBy);

    private static ClientSecretDto ToDto(ClientSecret s, DateTime now) => new(
        s.Id, s.ExpiresOn, s.RevokedOn, s.LastUsedOn, s.CreatedOn, s.CreatedBy,
        s.RevokedOn is not null ? "Revoked" : s.ExpiresOn <= now ? "Expired" : "Active");

    [GeneratedRegex("^[a-z0-9._-]{3,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
