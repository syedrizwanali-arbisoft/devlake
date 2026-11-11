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

/// <summary>A call to an upstream system (DevLake, Sophia or Workstream) did not succeed. The subclasses say what kind of problem it was.</summary>
public abstract class ExternalApiException : Exception
{
    protected ExternalApiException(string system, string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        SystemName = system;
        StatusCode = statusCode;
    }

    /// <summary>Which system the call was to, for example "DevLake".</summary>
    public string SystemName { get; }

    /// <summary>The HTTP status the system answered with, when it answered.</summary>
    public int? StatusCode { get; }
}

/// <summary>
/// The system refused our credentials, or we could not get or use a token for it. Retrying the same request will not help until the credentials
/// are fixed, so a caller should stop and leave work queued instead of failing individual requests.
/// </summary>
public sealed class ExternalAuthenticationException : ExternalApiException
{
    public ExternalAuthenticationException(string system, string message, int? statusCode = null, Exception? inner = null)
        : base(system, message, statusCode, inner)
    {
    }
}

/// <summary>
/// The system could not be reached or kept failing: a timeout, a connection error, a 5xx or 429 after every retry, or an open circuit breaker.
/// The request itself may be fine, so it can be tried again later.
/// </summary>
public sealed class ExternalApiUnavailableException : ExternalApiException
{
    public ExternalApiUnavailableException(string system, string message, int? statusCode = null, Exception? inner = null)
        : base(system, message, statusCode, inner)
    {
    }
}

/// <summary>The system understood the call and refused it (a 4xx other than an authentication failure). This request will not succeed as sent.</summary>
public sealed class ExternalApiRejectedException : ExternalApiException
{
    public ExternalApiRejectedException(string system, string message, int statusCode)
        : base(system, message, statusCode)
    {
    }
}

/// <summary>The system answered successfully but with something that is not what the contract says, such as a body that is not JSON.</summary>
public sealed class ExternalApiResponseException : ExternalApiException
{
    public ExternalApiResponseException(string system, string message, Exception? inner = null)
        : base(system, message, null, inner)
    {
    }
}

/// <summary>The client has no active outbound config or no active callback URL for the event type, so there is nowhere to send the result.</summary>
public sealed class CallbackNotConfiguredException : Exception
{
    public CallbackNotConfiguredException(string message)
        : base(message)
    {
    }
}
