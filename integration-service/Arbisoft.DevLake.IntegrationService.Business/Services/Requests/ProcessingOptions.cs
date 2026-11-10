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

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>Whether this process runs the processing job. Only the API runs it.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often the job takes the requests queued since its last run.</summary>
    public int IntervalSeconds { get; set; } = 60;

    public int MaxMessagesPerRun { get; set; } = 100;

    /// <summary>Caps concurrent DevLake calls, since a batch of 20 engineers can need hundreds.</summary>
    public int MaxParallelDevLakeCalls { get; set; } = 16;

    /// <summary>The wait before delivery attempt n + 1 is n times this value.</summary>
    public int DeliveryRetryDelaySeconds { get; set; } = 5;
}
