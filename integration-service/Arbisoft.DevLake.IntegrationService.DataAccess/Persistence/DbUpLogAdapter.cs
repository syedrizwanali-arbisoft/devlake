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

using System.Globalization;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;

/// <summary>Sends DbUp's progress messages to the application's logger.</summary>
public sealed class DbUpLogAdapter : IUpgradeLog
{
    private readonly ILogger _logger;

    public DbUpLogAdapter(ILogger logger)
    {
        _logger = logger;
    }

    public void LogTrace(string format, params object[] args) => Write(LogLevel.Trace, null, format, args);

    public void LogDebug(string format, params object[] args) => Write(LogLevel.Debug, null, format, args);

    public void LogInformation(string format, params object[] args) => Write(LogLevel.Information, null, format, args);

    public void LogWarning(string format, params object[] args) => Write(LogLevel.Warning, null, format, args);

    public void LogError(string format, params object[] args) => Write(LogLevel.Error, null, format, args);

    public void LogError(Exception ex, string format, params object[] args) => Write(LogLevel.Error, ex, format, args);

    private void Write(LogLevel level, Exception? exception, string format, object[] args)
    {
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var message = args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
        _logger.Log(level, exception, "{DbUpMessage}", message);
    }
}
