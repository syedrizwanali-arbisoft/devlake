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

/// <summary>
/// Delivers results to Sophia's callback API. Where to send them, and how to authenticate, come from the client's ClientConfig and
/// ClientConfigOutboundUrls, because each environment (Stage, Prod) has its own.
/// </summary>
public interface ISophiaClient
{
    /// <summary>
    /// Posts a result to the client's callback URL for <paramref name="eventType"/>. The access token is fetched when needed and reused until it
    /// expires; if Sophia rejects a cached token it is replaced once and the call repeated.
    /// </summary>
    /// <exception cref="CallbackNotConfiguredException">There is no active config or callback URL for the event type.</exception>
    /// <exception cref="ExternalAuthenticationException">A token could not be obtained or Sophia rejected it.</exception>
    /// <exception cref="ExternalApiUnavailableException">Sophia could not be reached.</exception>
    Task<SophiaDeliveryResult> PostCallbackAsync(long clientRefId, string eventType, string payloadJson, string correlationId, CancellationToken cancellationToken);
}
