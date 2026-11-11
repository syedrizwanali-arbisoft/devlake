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

public interface ITokenGenerator
{
    /// <summary>Issues a signed access token for a client with the given scopes.</summary>
    Task<IssuedToken> GenerateAsync(TokenSpecification specification, CancellationToken cancellationToken);
}

/// <summary>What to put in a token: who it is for, what it allows and how long it lasts.</summary>
public sealed record TokenSpecification(string ClientId, IReadOnlyCollection<string> Scopes, int LifetimeSeconds);

/// <summary>A signed token and the facts the Auth API stores about it in AccessTokens.</summary>
public sealed record IssuedToken(string AccessToken, string Jti, long SigningKeyId, DateTimeOffset IssuedOn, DateTimeOffset ExpiresOn);
