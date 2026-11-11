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

using Arbisoft.DevLake.IntegrationService.Business.Dtos.Auth;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Auth;

/// <summary>Why a client was not authenticated. It is logged but never shown to the caller, so credentials cannot be probed.</summary>
public enum AuthFailureReason
{
    UnknownClient,
    InactiveClient,
    InvalidSecret
}

/// <summary>The outcome of an authentication attempt: a token, or the reason it was refused.</summary>
public sealed record AuthResult(TokenResponse? Token, AuthFailureReason? Failure)
{
    public bool Succeeded => Token is not null;

    public static AuthResult Success(TokenResponse token) => new(token, null);

    public static AuthResult Fail(AuthFailureReason reason) => new(null, reason);
}
