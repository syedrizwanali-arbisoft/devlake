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

using Microsoft.Extensions.Logging;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>Collects every log message written during a test, so a test can check what was and was not logged.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public string All => string.Join('\n', Messages);

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }

    private sealed class Logger : ILogger
    {
        private readonly CapturedLogs _owner;

        public Logger(CapturedLogs owner)
        {
            _owner = owner;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _owner.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception.GetType().Name));
        }
    }
}
