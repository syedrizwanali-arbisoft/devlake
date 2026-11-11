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

using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public class EmailNormalizerTests
{
    private static readonly string[] Domains = ["arbisoft.com"];

    [Theory]
    [InlineData("jane.doe@arbisoft.com", "jane.doe@arbisoft.com")]
    [InlineData("  Jane.Doe@ARBISOFT.com  ", "jane.doe@arbisoft.com")]
    [InlineData("\tJANE+tag@Arbisoft.Com\n", "jane+tag@arbisoft.com")]
    [InlineData("o'brien@arbisoft.com", "o'brien@arbisoft.com")]
    [InlineData("a_b-c%d@arbisoft.com", "a_b-c%d@arbisoft.com")]
    public void TryNormalize_TrimsAndLowerCases_ThenAcceptsAValidAddress(string raw, string expected)
    {
        Assert.True(EmailNormalizer.TryNormalize(raw, Domains, out var normalized, out var error));

        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@arbisoft.com")]
    [InlineData("jane@")]
    [InlineData("jane doe@arbisoft.com")]
    [InlineData("jane@@arbisoft.com")]
    [InlineData("jane@arbisoft")]
    [InlineData("jane@.arbisoft.com")]
    [InlineData(".jane@arbisoft.com")]
    [InlineData("jane@arbisoft..com")]
    [InlineData("jane@arbisoft.com, other@arbisoft.com")]
    [InlineData("<jane@arbisoft.com>")]
    public void TryNormalize_RejectsMalformedAddresses(string? raw)
    {
        Assert.False(EmailNormalizer.TryNormalize(raw, Domains, out _, out var error));

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("jane@gmail.com")]
    [InlineData("jane@arbisoft.com.evil.example")]
    [InlineData("jane@sub.arbisoft.com")]
    [InlineData("jane@notarbisoft.com")]
    public void TryNormalize_RejectsAddressesOutsideTheAllowedDomains(string raw)
    {
        Assert.False(EmailNormalizer.TryNormalize(raw, Domains, out _, out var error));

        Assert.Equal("Only Arbisoft email addresses are accepted.", error);
    }

    [Fact]
    public void TryNormalize_AcceptsEveryConfiguredDomain_IgnoringCase()
    {
        Assert.True(EmailNormalizer.TryNormalize("a@Partner.example", ["arbisoft.com", "PARTNER.example"], out var normalized, out _));

        Assert.Equal("a@partner.example", normalized);
    }

    [Fact]
    public void TryNormalize_RejectsAnOverlongAddress()
    {
        var raw = new string('a', 250) + "@arbisoft.com";

        Assert.False(EmailNormalizer.TryNormalize(raw, Domains, out _, out _));
    }

    [Fact]
    public void TryNormalize_LowerCasesWithTheInvariantCulture_EvenUnderTheTurkishLocale()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            Assert.True(EmailNormalizer.TryNormalize("ILKER@ARBISOFT.COM", Domains, out var normalized, out _));

            Assert.Equal("ilker@arbisoft.com", normalized);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Normalize_ThrowsForAnAddressThatWasNotValidated()
    {
        Assert.Throws<ArgumentException>(() => EmailNormalizer.Normalize("nope", Domains));
        Assert.Equal("jane@arbisoft.com", EmailNormalizer.Normalize(" Jane@Arbisoft.com", Domains));
    }

    [Theory]
    [InlineData(" Jane.Doe@Gmail.COM ", "jane.doe@gmail.com")]
    [InlineData("12345+jane@users.noreply.github.com", "12345+jane@users.noreply.github.com")]
    public void TryNormalizeAnyDomain_NormalizesWhateverTheDomain(string raw, string expected)
    {
        Assert.True(EmailNormalizer.TryNormalizeAnyDomain(raw, out var normalized, out var error));
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("two@@gmail.com")]
    [InlineData("a b@gmail.com")]
    public void TryNormalizeAnyDomain_RejectsMalformedAddresses(string? raw)
    {
        Assert.False(EmailNormalizer.TryNormalizeAnyDomain(raw, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
