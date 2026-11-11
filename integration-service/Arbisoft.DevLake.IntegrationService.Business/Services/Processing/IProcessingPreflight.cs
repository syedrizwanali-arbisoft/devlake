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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

/// <summary>The outcome of the checks made before any request is taken off the queue.</summary>
/// <param name="Ready">True when processing can go ahead.</param>
/// <param name="Problem">When not ready, what is wrong, for the log.</param>
public sealed record PreflightResult(bool Ready, string? Problem)
{
    public static PreflightResult Go { get; } = new(true, null);

    public static PreflightResult Stop(string problem) => new(false, problem);
}

public interface IProcessingPreflight
{
    /// <summary>
    /// Checks that DevLake accepts our API token and that an access token can be obtained for every client that has requests waiting. Clients with
    /// nothing waiting are not checked, so an idle client's broken credentials stop nobody. Done before messages are taken, so a credentials problem
    /// leaves requests queued without raising any message's delivery count.
    /// </summary>
    Task<PreflightResult> RunAsync(CancellationToken cancellationToken);
}
