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

using Arbisoft.DevLake.IntegrationService.API.Middleware;
using Microsoft.AspNetCore.Http;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Middleware;

public class CorrelationIdMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(string? suppliedHeader)
    {
        var context = new DefaultHttpContext();
        if (suppliedHeader is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = suppliedHeader;
        }

        await new CorrelationIdMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ValidHeader_IsKeptAndReturned()
    {
        var context = await InvokeAsync("abc-123_X");

        Assert.Equal("abc-123_X", context.TraceIdentifier);
        Assert.Equal("abc-123_X", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_NoHeader_GeneratesAGuid()
    {
        var context = await InvokeAsync(null);

        Assert.True(Guid.TryParse(context.TraceIdentifier, out _));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("")]
    public async Task InvokeAsync_UnsafeHeader_IsReplaced(string supplied)
    {
        var context = await InvokeAsync(supplied);

        Assert.True(Guid.TryParse(context.TraceIdentifier, out _));
    }

    [Fact]
    public async Task InvokeAsync_OverlongHeader_IsReplaced()
    {
        var context = await InvokeAsync(new string('a', 65));

        Assert.True(Guid.TryParse(context.TraceIdentifier, out _));
    }
}
