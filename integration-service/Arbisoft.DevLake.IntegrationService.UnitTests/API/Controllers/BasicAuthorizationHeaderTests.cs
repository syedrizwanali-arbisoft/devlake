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

using System.Text;
using Arbisoft.DevLake.IntegrationService.API.Controllers;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API.Controllers;

public class BasicAuthorizationHeaderTests
{
    private static string Header(string value) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    [Fact]
    public void TryParse_ReadsTheClientIdAndSecret()
    {
        Assert.True(BasicAuthorizationHeader.TryParse(Header("sophia:s3cret"), out var id, out var secret));

        Assert.Equal("sophia", id);
        Assert.Equal("s3cret", secret);
    }

    [Fact]
    public void TryParse_KeepsColonsThatBelongToTheSecret()
    {
        Assert.True(BasicAuthorizationHeader.TryParse(Header("sophia:a:b:c"), out _, out var secret));

        Assert.Equal("a:b:c", secret);
    }

    [Fact]
    public void TryParse_SchemeNameIsCaseInsensitive()
    {
        Assert.True(BasicAuthorizationHeader.TryParse("basic " + Convert.ToBase64String("a:b"u8.ToArray()), out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic")]
    [InlineData("Basic !!!not-base64!!!")]
    public void TryParse_MissingOrMalformedHeader_IsRejected(string? header)
    {
        Assert.False(BasicAuthorizationHeader.TryParse(header, out _, out _));
    }

    [Theory]
    [InlineData("no-colon")]
    [InlineData(":secret-only")]
    [InlineData("id-only:")]
    public void TryParse_MissingIdOrSecret_IsRejected(string credentials)
    {
        Assert.False(BasicAuthorizationHeader.TryParse(Header(credentials), out _, out _));
    }
}
