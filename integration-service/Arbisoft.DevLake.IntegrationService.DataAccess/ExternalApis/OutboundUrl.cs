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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

/// <summary>
/// Builds the URLs of outbound calls from administrator-entered configuration, and refuses anything that could send data somewhere unintended
/// or over an unencrypted connection (SEC-T1).
/// </summary>
public static class OutboundUrl
{
    /// <summary>Joins a base URL and a path. The result stays on the base URL's host, so a path cannot redirect the call elsewhere.</summary>
    public static Uri Combine(string baseUrl, string path)
    {
        var baseUri = Parse(baseUrl, "base URL");
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal))
        {
            throw new CallbackNotConfiguredException("The outbound path must start with a single '/'.");
        }

        var combined = new Uri(baseUri, path);
        if (!string.Equals(combined.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) || combined.Port != baseUri.Port)
        {
            throw new CallbackNotConfiguredException("The outbound path must stay on the base URL's host.");
        }

        return combined;
    }

    /// <summary>Checks that a configured URL is absolute and uses HTTPS. Plain HTTP is allowed only for loopback addresses, for local development.</summary>
    public static Uri Parse(string url, string description)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new CallbackNotConfiguredException($"The configured {description} is not a valid absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new CallbackNotConfiguredException($"The configured {description} must use HTTPS.");
        }

        return uri;
    }
}
