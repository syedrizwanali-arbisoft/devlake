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

using Arbisoft.DevLake.IntegrationService.Business.Security;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Security;

public class ClientSecretHasherTests
{
    private readonly ClientSecretHasher _hasher = new();

    [Fact]
    public void Verify_CorrectSecret_Matches_AndWrongSecretDoesNot()
    {
        var hash = _hasher.Hash("s3cret");

        Assert.True(_hasher.Verify("s3cret", hash));
        Assert.False(_hasher.Verify("S3cret", hash));
        Assert.False(_hasher.Verify(string.Empty, hash));
    }

    [Fact]
    public void Hash_IsSaltedAndNeverContainsThePlainSecret()
    {
        var first = _hasher.Hash("s3cret");
        var second = _hasher.Hash("s3cret");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("s3cret", first, StringComparison.Ordinal);
        Assert.StartsWith("pbkdf2-sha256$", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$not-base64$AAAA")]
    [InlineData("md5$1000$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$AAAA$AAAA")]
    public void Verify_MalformedHash_NeverMatches(string hash)
    {
        Assert.False(_hasher.Verify("s3cret", hash));
    }
}
