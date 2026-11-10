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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;

/// <summary>How the service authenticates to the client's API.</summary>
public abstract record OutboundAuth;

public sealed record OAuth2ClientCredentials(string TokenUrl, string ClientId, string ClientSecret, string? Scope) : OutboundAuth
{
    // Keeps the secret out of logs and exception messages.
    public override string ToString() => $"OAuth2ClientCredentials {{ TokenUrl = {TokenUrl}, ClientId = {ClientId} }}";
}

public sealed record StaticApiToken(string Token) : OutboundAuth
{
    public override string ToString() => "StaticApiToken";
}

/// <summary>A client's API, built from its ClientConfigs row with the secrets already decrypted.</summary>
public sealed record SophiaConnection(long ClientConfigId, string BaseUrl, OutboundAuth Auth);

/// <summary>The outcome of one delivery attempt, kept for RequestResponseAttempts.</summary>
public sealed record CallbackResult(bool Succeeded, int? StatusCode, string ResponseBody, string? Error);

public interface ISophiaClient
{
    /// <summary>
    /// Returns a token for the connection, reusing a cached one until shortly before it expires. Throws an
    /// <see cref="ExternalApiAuthenticationException"/> when no token can be obtained.
    /// </summary>
    Task<string> GetAccessTokenAsync(SophiaConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the JSON to the callback. Delivery failures are returned rather than thrown, so the caller can record the
    /// attempt. This is a single attempt with no HTTP-level retry, because the caller counts attempts against maxAttempts.
    /// A token failure still throws.
    /// </summary>
    Task<CallbackResult> SendCallbackAsync(
        SophiaConnection connection, string method, string path, string json, string correlationId,
        CancellationToken cancellationToken = default);
}
