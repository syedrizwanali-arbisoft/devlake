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
using Arbisoft.DevLake.IntegrationService.Business.Services.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arbisoft.DevLake.IntegrationService.Business.Validation;

/// <summary>The list must hold between one and 20 emails, each well-formed and in an allowed domain.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class EmailListAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IReadOnlyCollection<string> emails || emails.Count == 0)
        {
            return new ValidationResult("At least one email is required.");
        }

        if (emails.Count > RequestLimits.MaxBatchEmails)
        {
            return new ValidationResult($"A batch can hold at most {RequestLimits.MaxBatchEmails} emails.");
        }

        var domains = validationContext.GetRequiredService<IOptionsSnapshot<RequestsOptions>>().Value.AllowedEmailDomains;
        var position = 0;
        foreach (var email in emails)
        {
            position++;
            if (!EmailNormalizer.TryNormalize(email, domains, out _, out var error))
            {
                return new ValidationResult($"Email {position} is not accepted: {error}");
            }
        }

        return ValidationResult.Success;
    }
}
