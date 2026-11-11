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

using System.ComponentModel.DataAnnotations;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Options;

/// <summary>Settings for the Integration MySQL database. The connection string comes from the environment, not appsettings.json.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>When true, pending SQL migration scripts are applied at startup. Only the API turns this on.</summary>
    public bool RunMigrationsOnStartup { get; init; }

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; init; } = 30;
}
