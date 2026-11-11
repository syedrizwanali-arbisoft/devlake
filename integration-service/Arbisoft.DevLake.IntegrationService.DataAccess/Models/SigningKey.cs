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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Models;

/// <summary>A key used to sign access tokens. PrivateKeyRef points into the secret store; the private key is never stored here.</summary>
public sealed class SigningKey
{
    public long Id { get; init; }
    public string Kid { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public string PublicKey { get; init; } = string.Empty;
    public string PrivateKeyRef { get; init; } = string.Empty;
    public DateTimeOffset ActivatedOn { get; init; }
    public DateTimeOffset? RetiredOn { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
}
