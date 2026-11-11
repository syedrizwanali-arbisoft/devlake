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

public interface IRequestService
{
    /// <summary>
    /// Logs a request and puts it on the queue, or logs it as revoked when the client has no active callback URL for it.
    /// Nothing is left half-done: if the queue rejects the message, the log rows are rolled back.
    /// </summary>
    /// <exception cref="DataAccess.Messaging.QueueFullException">The queue is full.</exception>
    /// <exception cref="DataAccess.Messaging.QueueUnavailableException">The broker is unreachable.</exception>
    Task<SubmissionResult> SubmitAsync(RequestSubmission submission, CancellationToken cancellationToken);
}
