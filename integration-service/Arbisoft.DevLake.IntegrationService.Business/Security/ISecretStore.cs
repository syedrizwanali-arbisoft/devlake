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

namespace Arbisoft.DevLake.IntegrationService.Business.Security;

/// <summary>
/// Holds secrets by reference, such as the private signing keys named by <c>SigningKeys.privateKeyRef</c>.
/// A configuration or environment value <c>Secrets__&lt;ref&gt;</c> takes precedence over the encrypted database copy,
/// so a deployment can move a key into a vault without a code change.
/// </summary>
public interface ISecretStore
{
    /// <summary>The secret for <paramref name="secretRef"/>, or null when none exists.</summary>
    Task<string?> GetAsync(string secretRef, CancellationToken cancellationToken);

    /// <summary>Encrypts and saves a secret in the database, replacing any earlier value for the reference.</summary>
    Task SaveAsync(string secretRef, string value, CancellationToken cancellationToken);
}
