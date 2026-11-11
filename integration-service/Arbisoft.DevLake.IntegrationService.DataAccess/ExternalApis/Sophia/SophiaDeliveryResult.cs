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

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Sophia;

/// <summary>
/// What Sophia's callback answered. Any HTTP answer is a result, successful or not, so the processing job can record every attempt with its
/// response; only a failure to reach Sophia or to authenticate is an exception.
/// </summary>
/// <param name="Delivered">True for a 2xx answer.</param>
/// <param name="StatusCode">The HTTP status Sophia answered with.</param>
/// <param name="ResponseBody">Sophia's answer, cut to a length that is safe to store.</param>
public sealed record SophiaDeliveryResult(bool Delivered, int StatusCode, string ResponseBody);
