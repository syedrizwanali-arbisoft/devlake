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

using System.Security.Claims;
using Arbisoft.DevLake.IntegrationService.Business.Security;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;

namespace Arbisoft.DevLake.IntegrationService.API.Authorization;

public sealed class TokenRevocationValidator(IAuthService authService)
{
    public async Task<bool> IsActiveAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var jti = principal?.FindFirst(TokenClaimNames.Jti)?.Value;
        return !string.IsNullOrEmpty(jti) && await authService.IsTokenActiveAsync(jti, cancellationToken);
    }
}
