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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Models;

/// <summary>One attempt to deliver a response to the client.</summary>
public sealed class RequestResponseAttempt
{
    public long Id { get; set; }
    public long RequestLogId { get; set; }
    public string ResponseJson { get; set; } = string.Empty;
    public string AttemptStatus { get; set; } = string.Empty;
    public string? FailureError { get; set; }
    public string AttemptBy { get; set; } = string.Empty;
    public DateTime AttemptOn { get; set; }
}
