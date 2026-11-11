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

using Arbisoft.DevLake.IntegrationService.DataAccess.Models;

namespace Arbisoft.DevLake.IntegrationService.Business.Services.Requests;

/// <summary>
/// The statuses the poll API reports, which are case-sensitive in the agreed contract. They are fewer than the statuses a request log moves
/// through, so each log status maps to one of these.
/// </summary>
public static class PollStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Success = "Success";
    public const string Failure = "Failure";
    public const string Revoked = "Revoked";

    /// <summary>Maps a <see cref="RequestStatus"/> value to the status shown to the client. A status that is not in the table is a defect, not a guess.</summary>
    public static string From(string requestLogStatus)
    {
        return requestLogStatus switch
        {
            RequestStatus.Received or RequestStatus.Queued => Queued,
            RequestStatus.Processing or RequestStatus.Processed => Processing,
            RequestStatus.Success => Success,
            RequestStatus.Failed => Failure,
            RequestStatus.RevokedNoCallbackUrl => Revoked,
            _ => throw new InvalidOperationException($"The request status '{requestLogStatus}' has no poll status.")
        };
    }
}
