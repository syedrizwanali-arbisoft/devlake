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

/// <summary>Holds secrets that must stay out of the main tables, such as signing private keys.</summary>
public interface ISecretStore
{
    /// <summary>Returns the secret, or null if the reference is unknown.</summary>
    Task<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Saves a new secret under a reference that hasn't been used before.</summary>
    Task SaveSecretAsync(string reference, string value, string createdBy, CancellationToken cancellationToken = default);
}
