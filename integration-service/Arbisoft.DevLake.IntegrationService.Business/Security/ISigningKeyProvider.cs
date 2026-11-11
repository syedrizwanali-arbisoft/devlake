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

using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

public interface ISigningKeyProvider
{
    /// <summary>The key new tokens are signed with, including its private half.</summary>
    /// <exception cref="SigningKeyUnavailableException">There is no active key, or its private key cannot be read.</exception>
    Task<ActiveSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken);

    /// <summary>The public key for <paramref name="kid"/>, including retired keys so older tokens still verify. Null when the kid is unknown.</summary>
    Task<SecurityKey?> GetVerificationKeyAsync(string kid, CancellationToken cancellationToken);
}

/// <summary>The active signing key: its database id (stored with each issued token), its kid, and the key to sign with.</summary>
public sealed record ActiveSigningKey(long Id, string Kid, SecurityKey Key);
