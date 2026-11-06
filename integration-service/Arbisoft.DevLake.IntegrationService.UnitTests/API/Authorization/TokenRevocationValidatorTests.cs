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
using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.Business.Services.Auth;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Authorization;

public class TokenRevocationValidatorTests
{
    private readonly IAuthService _authService = Substitute.For<IAuthService>();

    private static ClaimsPrincipal PrincipalWithJti(string jti) => new(new ClaimsIdentity([new Claim("jti", jti)], "test"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReturnsWhatTheAuthServiceReports(bool active)
    {
        _authService.IsTokenActiveAsync("the-jti", Arg.Any<CancellationToken>()).Returns(active);

        var result = await new TokenRevocationValidator(_authService).IsActiveAsync(PrincipalWithJti("the-jti"), CancellationToken.None);

        Assert.Equal(active, result);
    }

    [Fact]
    public async Task RejectsATokenWithoutAJti()
    {
        var validator = new TokenRevocationValidator(_authService);

        Assert.False(await validator.IsActiveAsync(new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None));
        Assert.False(await validator.IsActiveAsync(null, CancellationToken.None));
    }
}
