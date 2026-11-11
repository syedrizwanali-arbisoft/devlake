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

/// <summary>A stats request on the queue: the JSON body, the correlation ID the client got back, and the event type used to find the callback URL.</summary>
public sealed record QueueMessage(string CorrelationId, string EventType, string Json);

/// <summary>A message taken from the queue, with what the broker knows about it.</summary>
/// <param name="Message">The request.</param>
/// <param name="DeliveryTag">Identifies this delivery when acknowledging it.</param>
/// <param name="Redelivered">True when the broker has delivered this message before.</param>
/// <param name="DeliveryCount">How many times it was delivered before this delivery.</param>
public sealed record QueueDelivery(QueueMessage Message, ulong DeliveryTag, bool Redelivered, int DeliveryCount);
