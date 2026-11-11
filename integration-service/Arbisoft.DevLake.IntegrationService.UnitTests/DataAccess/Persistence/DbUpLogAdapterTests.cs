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

using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

public class DbUpLogAdapterTests
{
    private readonly RecordingLogger _logger = new();
    private readonly DbUpLogAdapter _adapter;

    public DbUpLogAdapterTests()
    {
        _adapter = new DbUpLogAdapter(_logger);
    }

    [Fact]
    public void EachDbUpLevel_IsWrittenAtTheMatchingLogLevel()
    {
        _adapter.LogTrace("trace");
        _adapter.LogDebug("debug");
        _adapter.LogInformation("information");
        _adapter.LogWarning("warning");
        _adapter.LogError("error");

        Assert.Equal(
            [(LogLevel.Trace, "trace"), (LogLevel.Debug, "debug"), (LogLevel.Information, "information"), (LogLevel.Warning, "warning"), (LogLevel.Error, "error")],
            _logger.Entries.Select(e => (e.Level, e.Message)));
    }

    [Fact]
    public void AnErrorWithAnException_KeepsTheException()
    {
        var exception = new InvalidOperationException("boom");

        _adapter.LogError(exception, "script {0} failed", "005");

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal("script 005 failed", entry.Message);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public void AMessageWithBracesAndNoArguments_IsLoggedAsItIs()
    {
        _adapter.LogInformation("applied {literal}");

        Assert.Equal("applied {literal}", Assert.Single(_logger.Entries).Message);
    }

    [Fact]
    public void ADisabledLevel_WritesNothing()
    {
        _logger.MinimumLevel = LogLevel.Warning;

        _adapter.LogInformation("quiet");
        _adapter.LogWarning("loud");

        Assert.Equal("loud", Assert.Single(_logger.Entries).Message);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.First(p => p.Key == "DbUpMessage").Value?.ToString() ?? string.Empty : formatter(state, exception);
            Entries.Add((logLevel, message, exception));
        }
    }
}
