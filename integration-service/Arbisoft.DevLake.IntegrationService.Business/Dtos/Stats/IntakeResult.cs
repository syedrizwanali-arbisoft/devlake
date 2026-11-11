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

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Stats;

/// <summary>How an intake request ended.</summary>
public enum IntakeOutcome
{
    /// <summary>The request is queued, or an identical one already was.</summary>
    Accepted,

    /// <summary>The client has no active callback URL for this request, so nothing was queued.</summary>
    NoCallbackUrl,

    /// <summary>The caller's client no longer exists or is not active.</summary>
    UnknownClient
}

/// <summary>The outcome and the acknowledgement to return. The acknowledgement is null only for <see cref="IntakeOutcome.UnknownClient"/>.</summary>
public sealed record IntakeResult<TAcknowledgement>(IntakeOutcome Outcome, TAcknowledgement? Acknowledgement);
