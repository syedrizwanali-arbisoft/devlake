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

namespace Arbisoft.DevLake.IntegrationService.API.Controllers;

/// <summary>Reads <c>Authorization: Basic base64(client_id:client_secret)</c>. The secret may itself contain colons.</summary>
public static class BasicAuthorizationHeader
{
    private const string Scheme = "Basic ";
    private const char Separator = ':';

    public static bool TryParse(string? header, out string clientId, out string clientSecret)
    {
        clientId = string.Empty;
        clientSecret = string.Empty;

        if (header is null || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[Scheme.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var split = decoded.IndexOf(Separator, StringComparison.Ordinal);
        if (split <= 0 || split == decoded.Length - 1)
        {
            return false;
        }

        clientId = decoded[..split];
        clientSecret = decoded[(split + 1)..];
        return true;
    }
}
