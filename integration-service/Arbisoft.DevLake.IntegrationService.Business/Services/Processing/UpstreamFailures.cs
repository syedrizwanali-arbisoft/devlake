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

using Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

/// <summary>Waits for a group of calls and, when some failed, reports the one that matters most.</summary>
internal static class UpstreamFailures
{
    /// <summary>
    /// Waits for every call, then throws the most serious failure: a credentials problem outranks an unreachable system, which outranks a refused
    /// request. Task.WhenAll alone would report whichever failed first, which could hide a problem that should stop the whole run.
    /// </summary>
    public static async Task AwaitAllAsync<T>(IReadOnlyList<Task<T>> calls)
    {
        try
        {
            await Task.WhenAll(calls);
        }
        catch (Exception)
        {
            var failures = calls.Where(call => call.IsFaulted).Select(call => call.Exception!.InnerException!).ToList();
            if (failures.Count == 0)
            {
                throw;
            }

            throw failures.OrderBy(Severity).First();
        }
    }

    private static int Severity(Exception exception) => exception switch
    {
        ExternalAuthenticationException => 0,
        ExternalApiUnavailableException => 1,
        ExternalApiException => 2,
        _ => 3
    };
}
