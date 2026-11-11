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
using Serilog.Context;

namespace Arbisoft.DevLake.IntegrationService.API.Middleware;

/// <summary>
/// Accepts or generates a correlation ID at the edge, writes it into the logging scope, and returns it in the response.
/// A caller-supplied value is only trusted when it is short and made of safe characters.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private const string LogPropertyName = "CorrelationId";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await _next(context);
        }
    }

    private static string ReadOrCreate(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        return supplied is { Length: > 0 and <= MaxLength } && SafeValue().IsMatch(supplied)
            ? supplied
            : Guid.NewGuid().ToString("D");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeValue();
}
