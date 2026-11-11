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

using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.ExternalApis;

public class OutboundUrlTests
{
    [Theory]
    [InlineData("https://sophia.example.com", "/api/v1/devlake/callback/", "https://sophia.example.com/api/v1/devlake/callback/")]
    [InlineData("https://sophia.example.com/", "/cb", "https://sophia.example.com/cb")]
    [InlineData("https://sophia.example.com:8443", "/cb", "https://sophia.example.com:8443/cb")]
    [InlineData("http://localhost:5097", "/cb", "http://localhost:5097/cb")]
    [InlineData("http://127.0.0.1:5097", "/cb", "http://127.0.0.1:5097/cb")]
    public void Combine_JoinsABaseAndAPath(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, OutboundUrl.Combine(baseUrl, path).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://sophia.example.com", "//evil.example/x")]
    [InlineData("https://sophia.example.com", "cb")]
    [InlineData("https://sophia.example.com", "/a\\b")]
    [InlineData("https://sophia.example.com", "")]
    [InlineData("http://sophia.example.com", "/cb")]
    [InlineData("ftp://sophia.example.com", "/cb")]
    [InlineData("not a url", "/cb")]
    public void Combine_RefusesAnythingThatCouldLeaveTheHostOrSkipEncryption(string baseUrl, string path)
    {
        Assert.Throws<CallbackNotConfiguredException>(() => OutboundUrl.Combine(baseUrl, path));
    }

    [Theory]
    [InlineData("https://sophia.example.com/oauth2/token/", true)]
    [InlineData("http://localhost/token", true)]
    [InlineData("http://[::1]:8080/token", true)]
    [InlineData("http://sophia.example.com/token", false)]
    [InlineData("http://localhost.evil.example/token", false)]
    [InlineData("/relative/token", false)]
    [InlineData("", false)]
    public void Parse_AcceptsHttpsAndLoopbackOnly(string url, bool accepted)
    {
        if (accepted)
        {
            Assert.NotNull(OutboundUrl.Parse(url, "token URL"));
        }
        else
        {
            Assert.Throws<CallbackNotConfiguredException>(() => OutboundUrl.Parse(url, "token URL"));
        }
    }
}
