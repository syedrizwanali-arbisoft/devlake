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

using System.Text.RegularExpressions;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>
/// The only place an email address is normalized or validated (MAP-7). The steps run in a fixed order: trim, lower-case with the invariant
/// culture, then validate the normalized value (MAP-1, MAP-2, MAP-6). Everything is pure, so it is tested in isolation (MAP-8).
/// Compare normalized emails with <see cref="StringComparer.OrdinalIgnoreCase"/> (MAP-3).
/// </summary>
public static partial class EmailNormalizer
{
    /// <summary>Trims and lower-cases <paramref name="raw"/> and checks it is a well-formed address in one of <paramref name="allowedDomains"/>.</summary>
    public static bool TryNormalize(string? raw, IEnumerable<string> allowedDomains, out string normalized, out string? error)
    {
        normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length == 0)
        {
            error = "The email is required.";
            return false;
        }

        if (normalized.Length > RequestLimits.MaxEmailLength || !Shape().IsMatch(normalized))
        {
            error = "The email is not a valid email address.";
            return false;
        }

        var domain = normalized[(normalized.IndexOf('@', StringComparison.Ordinal) + 1)..];
        if (!allowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            error = "Only Arbisoft email addresses are accepted.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Normalizes an address that has already been validated. Throws if it is not valid, which means validation was skipped.</summary>
    public static string Normalize(string raw, IEnumerable<string> allowedDomains)
    {
        return TryNormalize(raw, allowedDomains, out var normalized, out var error)
            ? normalized
            : throw new ArgumentException(error, nameof(raw));
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._%+'\-]*@[a-z0-9]([a-z0-9\-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9\-]*[a-z0-9])?)+$")]
    private static partial Regex Shape();
}
