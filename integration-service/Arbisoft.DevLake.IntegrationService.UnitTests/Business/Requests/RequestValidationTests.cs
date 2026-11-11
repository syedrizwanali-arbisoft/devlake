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

using System.ComponentModel.DataAnnotations;
using Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Requests;

public sealed class RequestValidationTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddOptions<RequestsOptions>().Services.BuildServiceProvider();

    public void Dispose()
    {
        _services.Dispose();
    }

    private List<ValidationResult> Validate(object instance) => Validate(instance, _services);

    private static List<ValidationResult> Validate(object instance, IServiceProvider services)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance, services, items: null), results, validateAllProperties: true);
        return results;
    }

    private sealed class FixedSnapshot : IOptionsSnapshot<RequestsOptions>
    {
        public FixedSnapshot(RequestsOptions value)
        {
            Value = value;
        }

        public RequestsOptions Value { get; }

        public RequestsOptions Get(string? name) => Value;
    }

    private static string[] Emails(int count) => Enumerable.Range(1, count).Select(i => $"user{i}@arbisoft.com").ToArray();

    [Fact]
    public void ABatchWithValidEmailsAndTimeFrame_Passes()
    {
        Assert.Empty(Validate(new BatchStatsRequest { Emails = ["a@arbisoft.com", " B@Arbisoft.com "], TimeFrame = "24h" }));
    }

    [Fact]
    public void ABatchOfExactlyTwentyEmails_Passes_AndTwentyOneIsRejected()
    {
        Assert.Empty(Validate(new BatchStatsRequest { Emails = Emails(20), TimeFrame = "24h" }));

        var tooMany = Assert.Single(Validate(new BatchStatsRequest { Emails = Emails(21), TimeFrame = "24h" }));
        Assert.Contains("at most 20", tooMany.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ABatchWithNoEmails_OrNullEmails_IsRejected()
    {
        Assert.NotEmpty(Validate(new BatchStatsRequest { Emails = [], TimeFrame = "24h" }));
        Assert.NotEmpty(Validate(new BatchStatsRequest { Emails = null, TimeFrame = "24h" }));
    }

    [Fact]
    public void ABadEmail_IsReportedByPosition_WithoutEchoingTheAddress()
    {
        var error = Assert.Single(Validate(new BatchStatsRequest { Emails = ["a@arbisoft.com", "secret.person@gmail.com"], TimeFrame = "24h" }));

        Assert.Contains("Email 2", error.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.person", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24H")]
    [InlineData("2y")]
    public void ABatchWithAMissingOrInvalidTimeFrame_IsRejected(string? timeFrame)
    {
        Assert.NotEmpty(Validate(new BatchStatsRequest { Emails = ["a@arbisoft.com"], TimeFrame = timeFrame }));
    }

    [Fact]
    public void TheEmailDomainListComesFromConfiguration()
    {
        var snapshot = new FixedSnapshot(new RequestsOptions { AllowedEmailDomains = ["partner.example"] });
        using var services = new ServiceCollection().AddSingleton<IOptionsSnapshot<RequestsOptions>>(snapshot).BuildServiceProvider();
        var request = new BatchStatsRequest { Emails = ["a@partner.example"], TimeFrame = "24h" };
        var other = new BatchStatsRequest { Emails = ["a@arbisoft.com"], TimeFrame = "24h" };

        Assert.Empty(Validate(request, services));
        Assert.NotEmpty(Validate(other, services));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(15, false)]
    [InlineData(1440, false)]
    [InlineData(1441, true)]
    public void TheDuplicateWindow_MustBeBetweenOneMinuteAndOneDay(int minutes, bool invalid)
    {
        var results = Validate(new RequestsOptions { DuplicateWindowMinutes = minutes });

        Assert.Equal(invalid, results.Count > 0);
    }

    [Fact]
    public void TheDefaults_AreFifteenMinutes_AndTheArbisoftDomain()
    {
        var options = new RequestsOptions();

        Assert.Equal(15, options.DuplicateWindowMinutes);
        Assert.Equal(["arbisoft.com"], options.AllowedEmailDomains);
    }
}
