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

using System.Text.Json;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.ExternalApis.Workstream;

/// <summary>One page of Workstream's project list.</summary>
/// <param name="Items">The projects on this page, as Workstream returned them.</param>
/// <param name="Page">The page number that was asked for, starting at 1.</param>
/// <param name="PageSize">The page size that was asked for. A page with fewer items than this is the last one.</param>
public sealed record WorkstreamProjectsPage(IReadOnlyList<JsonElement> Items, int Page, int PageSize)
{
    private static readonly string[] ListProperties = ["results", "data", "items", "projects"];

    public bool IsLast => Items.Count < PageSize;

    /// <summary>
    /// Reads a page from either shape Workstream may use: a bare JSON array, or an object holding the array under results, data, items or projects.
    /// Returns null when the body has neither.
    /// </summary>
    public static WorkstreamProjectsPage? FromJson(JsonElement body, int page, int pageSize)
    {
        if (body.ValueKind == JsonValueKind.Array)
        {
            return new WorkstreamProjectsPage(body.EnumerateArray().ToList(), page, pageSize);
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in ListProperties)
        {
            if (body.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                return new WorkstreamProjectsPage(list.EnumerateArray().ToList(), page, pageSize);
            }
        }

        return null;
    }
}
