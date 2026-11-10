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

namespace Arbisoft.DevLake.IntegrationService.Business.Dtos.Admin;

/// <summary>The outcome of an admin change. Error is a message to show the admin.</summary>
public sealed record AdminResult(bool Succeeded, string? Error = null)
{
    public static readonly AdminResult Ok = new(true);
    public static AdminResult Fail(string error) => new(false, error);
}

public sealed record AdminResult<T>(bool Succeeded, T? Value, string? Error = null)
{
    public static AdminResult<T> Ok(T value) => new(true, value);
    public static AdminResult<T> Fail(string error) => new(false, default, error);
}
