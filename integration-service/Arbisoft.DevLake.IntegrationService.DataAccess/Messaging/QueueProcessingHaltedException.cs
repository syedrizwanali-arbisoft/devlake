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

/// <summary>
/// Thrown by a consume handler when a problem affects every message, such as a rejected token or an unreachable
/// dependency. The consumer stops the run and leaves the message queued, without counting it as that message's failure.
/// </summary>
public sealed class QueueProcessingHaltedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
