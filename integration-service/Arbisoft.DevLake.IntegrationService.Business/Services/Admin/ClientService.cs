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
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Arbisoft.DevLake.IntegrationService.DataAccess.Repositories;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Admin;

public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken cancellationToken);

    Task<ClientDetailsDto?> GetAsync(long id, CancellationToken cancellationToken);

    Task<AdminResult<long>> CreateAsync(ClientInput input, string createdBy, CancellationToken cancellationToken);

    /// <summary>Updates the name, active flag and token lifetime. The Client ID never changes.</summary>
    Task<AdminResult> UpdateAsync(long id, ClientInput input, string modifiedBy, CancellationToken cancellationToken);

    /// <summary>Creates a secret that is valid for the given number of days. A client can hold several at once, so a secret can be rotated without downtime.</summary>
    Task<AdminResult<NewClientSecretDto>> CreateSecretAsync(long id, int validForDays, string createdBy, CancellationToken cancellationToken);

    Task<AdminResult> RevokeSecretAsync(long id, long secretId, CancellationToken cancellationToken);

    /// <summary>Replaces the scopes a client may request.</summary>
    Task<AdminResult> SetScopesAsync(long id, IReadOnlyCollection<long> scopeIds, string changedBy, CancellationToken cancellationToken);
}

public sealed partial class ClientService : IClientService
{
    public const int MinLifetimeSeconds = 60;
    public const int MaxLifetimeSeconds = 86_400;
    public const int MaxSecretDays = 730;
    public const int MaxNameLength = 200;
    private const int SecretBytes = 32;

    private readonly IClientRepository _clients;
    private readonly IScopeRepository _scopes;
    private readonly IClientSecretHasher _hasher;
    private readonly IDbSession _session;
    private readonly TimeProvider _timeProvider;

    public ClientService(IClientRepository clients, IScopeRepository scopes, IClientSecretHasher hasher, IDbSession session, TimeProvider timeProvider)
    {
        _clients = clients;
        _scopes = scopes;
        _hasher = hasher;
        _session = session;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken cancellationToken)
    {
        var page = await _clients.ListAsync(1, AdminPaging.MaxRows, cancellationToken);
        return page.Items.Select(ToDto).ToList();
    }

    public async Task<ClientDetailsDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var secrets = await _clients.GetSecretsAsync(id, cancellationToken);
        var granted = (await _clients.GetClientScopesAsync(id, cancellationToken)).Select(s => s.ScopeId).ToHashSet();
        var allScopes = (await _scopes.ListAsync(1, AdminPaging.MaxRows, cancellationToken)).Items.Select(s => new ScopeDto(s.Id, s.Name, s.Description, s.CreatedOn)).ToList();
        return new ClientDetailsDto(
            ToDto(client),
            secrets.Select(s => new ClientSecretDto(s.Id, s.ExpiresOn, s.RevokedOn, s.LastUsedOn, s.CreatedOn, s.CreatedBy, SecretStatus(s, now))).ToList(),
            allScopes.Where(s => granted.Contains(s.Id)).ToList(),
            allScopes);
    }

    public async Task<AdminResult<long>> CreateAsync(ClientInput input, string createdBy, CancellationToken cancellationToken)
    {
        var problem = Validate(input, checkClientId: true);
        if (problem is not null)
        {
            return AdminResult.Failure<long>(problem);
        }

        if (await _clients.GetByClientIdAsync(input.ClientId, cancellationToken) is not null)
        {
            return AdminResult.Failure<long>($"A client with the ID '{input.ClientId}' already exists.");
        }

        var id = await _clients.InsertAsync(
            new Client
            {
                ClientId = input.ClientId,
                Name = input.Name.Trim(),
                IsActive = input.IsActive,
                AccessTokenLifetimeSeconds = input.AccessTokenLifetimeSeconds,
                CreatedOn = _timeProvider.GetUtcNow(),
                CreatedBy = createdBy
            },
            cancellationToken);
        return AdminResult.Success(id);
    }

    public async Task<AdminResult> UpdateAsync(long id, ClientInput input, string modifiedBy, CancellationToken cancellationToken)
    {
        var problem = Validate(input, checkClientId: false);
        if (problem is not null)
        {
            return AdminResult.Fail(problem);
        }

        var existing = await _clients.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return AdminResult.Fail("The client does not exist.");
        }

        await _clients.UpdateAsync(Modified(existing, input.Name.Trim(), input.IsActive, input.AccessTokenLifetimeSeconds, modifiedBy), cancellationToken);
        return AdminResult.Ok;
    }

    public async Task<AdminResult<NewClientSecretDto>> CreateSecretAsync(long id, int validForDays, string createdBy, CancellationToken cancellationToken)
    {
        if (validForDays is < 1 or > MaxSecretDays)
        {
            return AdminResult.Failure<NewClientSecretDto>($"A secret must be valid for 1 to {MaxSecretDays} days.");
        }

        var client = await _clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return AdminResult.Failure<NewClientSecretDto>("The client does not exist.");
        }

        var now = _timeProvider.GetUtcNow();
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresOn = now.AddDays(validForDays);
        var secretId = await _clients.InsertSecretAsync(
            new ClientSecret { ClientRefId = id, Secret = _hasher.Hash(plain), ExpiresOn = expiresOn, CreatedOn = now, CreatedBy = createdBy },
            cancellationToken);
        return AdminResult.Success(new NewClientSecretDto(secretId, client.ClientId, plain, expiresOn));
    }

    public async Task<AdminResult> RevokeSecretAsync(long id, long secretId, CancellationToken cancellationToken)
    {
        var secrets = await _clients.GetSecretsAsync(id, cancellationToken);
        var secret = secrets.FirstOrDefault(s => s.Id == secretId);
        if (secret is null)
        {
            return AdminResult.Fail("The secret does not belong to this client.");
        }

        if (secret.RevokedOn is not null)
        {
            return AdminResult.Fail("The secret is already revoked.");
        }

        return await _clients.RevokeSecretAsync(secretId, _timeProvider.GetUtcNow(), cancellationToken)
            ? AdminResult.Ok
            : AdminResult.Fail("The secret is already revoked.");
    }

    public async Task<AdminResult> SetScopesAsync(long id, IReadOnlyCollection<long> scopeIds, string changedBy, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return AdminResult.Fail("The client does not exist.");
        }

        var wanted = scopeIds.Distinct().ToList();
        var known = (await _scopes.ListAsync(1, AdminPaging.MaxRows, cancellationToken)).Items.Select(s => s.Id).ToHashSet();
        if (wanted.Any(scopeId => !known.Contains(scopeId)))
        {
            return AdminResult.Fail("One of the scopes does not exist.");
        }

        var now = _timeProvider.GetUtcNow();
        await _session.BeginTransactionAsync(cancellationToken);
        try
        {
            await _clients.ReplaceScopesAsync(id, wanted, changedBy, now, cancellationToken);
            await _clients.UpdateAsync(Modified(client, client.Name, client.IsActive, client.AccessTokenLifetimeSeconds, changedBy), cancellationToken);
            await _session.CommitAsync(cancellationToken);
        }
        catch
        {
            await _session.RollbackAsync(cancellationToken);
            throw;
        }

        return AdminResult.Ok;
    }

    private Client Modified(Client existing, string name, bool isActive, int lifetimeSeconds, string modifiedBy) => new()
    {
        Id = existing.Id,
        ClientId = existing.ClientId,
        Name = name,
        IsActive = isActive,
        AccessTokenLifetimeSeconds = lifetimeSeconds,
        CreatedOn = existing.CreatedOn,
        CreatedBy = existing.CreatedBy,
        ModifiedOn = _timeProvider.GetUtcNow(),
        ModifiedBy = modifiedBy
    };

    private static string? Validate(ClientInput input, bool checkClientId)
    {
        if (checkClientId && !ClientIdPattern().IsMatch(input.ClientId ?? string.Empty))
        {
            return "The client ID must be 3 to 128 characters: lowercase letters, digits and . _ -";
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > MaxNameLength)
        {
            return $"The name is required and can be at most {MaxNameLength} characters.";
        }

        return input.AccessTokenLifetimeSeconds is < MinLifetimeSeconds or > MaxLifetimeSeconds
            ? $"The token lifetime must be {MinLifetimeSeconds} to {MaxLifetimeSeconds} seconds."
            : null;
    }

    private static string SecretStatus(ClientSecret secret, DateTimeOffset now)
    {
        if (secret.RevokedOn is not null)
        {
            return "Revoked";
        }

        return secret.ExpiresOn is not null && secret.ExpiresOn <= now ? "Expired" : "Active";
    }

    private static ClientDto ToDto(Client c) =>
        new(c.Id, c.ClientId, c.Name, c.IsActive, c.AccessTokenLifetimeSeconds, c.CreatedOn, c.CreatedBy, c.ModifiedOn, c.ModifiedBy);

    [GeneratedRegex("^[a-z0-9._-]{3,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
