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

using System.Net;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

/// <summary>A call to DevLake, Sophia or Workstream failed. The message names the system and, when known, the status.</summary>
public class ExternalApiException(string system, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception($"{system}: {message}", innerException)
{
    public string System { get; } = system;
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>The service couldn't get or use a token for the system: missing settings, a rejected token, or a failed token request.</summary>
public sealed class ExternalApiAuthenticationException(string system, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : ExternalApiException(system, message, statusCode, innerException);
