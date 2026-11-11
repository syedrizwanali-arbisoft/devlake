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
using Microsoft.AspNetCore.Authorization;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Authorization;

public class ScopeAuthorizationHandlerTests
{
    private static async Task<bool> SucceedsAsync(string required, params string[] scopeClaims)
    {
        var identity = new ClaimsIdentity(scopeClaims.Select(value => new Claim("scope", value)), authenticationType: "Bearer");
        var requirement = new ScopeRequirement(required);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), resource: null);

        await new ScopeAuthorizationHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_WhenTheScopeIsAmongTheGrantedOnes()
    {
        Assert.True(await SucceedsAsync("stats:read", "projects:fetch stats:read"));
    }

    [Fact]
    public async Task Succeeds_WhenTheScopeIsInAnyOfSeveralClaims()
    {
        Assert.True(await SucceedsAsync("stats:read", "projects:fetch", "stats:read"));
    }

    [Theory]
    [InlineData("projects:fetch")]
    [InlineData("stats:read:all")]
    [InlineData("stats")]
    [InlineData("Stats:Read")]
    [InlineData("")]
    public async Task Fails_WhenTheScopeIsMissing_OrOnlyLooksSimilar(string granted)
    {
        Assert.False(await SucceedsAsync("stats:read", granted));
    }

    [Fact]
    public async Task Fails_WhenThereIsNoScopeClaimAtAll()
    {
        Assert.False(await SucceedsAsync("stats:read"));
    }
}
