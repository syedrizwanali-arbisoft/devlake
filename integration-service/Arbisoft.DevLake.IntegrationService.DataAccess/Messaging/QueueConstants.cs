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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Messaging;

/// <summary>Names and versions that cross the process boundary between the API and the processing job.</summary>
public static class QueueConstants
{
    /// <summary>The header that carries the message schema version (INT-M1).</summary>
    public const string SchemaVersionHeader = "schema-version";

    /// <summary>The header a quorum queue sets with how many times the message was delivered before this delivery.</summary>
    public const string AcquiredCountHeader = "x-acquired-count";

    /// <summary>The newest schema this code reads. A message with a higher or missing version goes to the dead-letter queue.</summary>
    public const int CurrentSchemaVersion = 1;

    public const string JsonContentType = "application/json";
    public const string DeadLetterExchangeSuffix = ".dlx";
    public const string DeadLetterQueueSuffix = ".dead";
}
