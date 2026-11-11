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

using System.Data;
using Arbisoft.DevLake.IntegrationService.DataAccess.Persistence;
using NSubstitute;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.DataAccess.Persistence;

public class DateTimeOffsetTypeHandlerTests
{
    private readonly DateTimeOffsetTypeHandler _handler = new();

    [Fact]
    public void Parse_ReadsAnUnspecifiedDateTimeAsUtc()
    {
        var stored = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Unspecified);

        var parsed = _handler.Parse(stored);

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public void SetValue_WritesTheUtcInstant_WhateverTheOffset()
    {
        var parameter = Substitute.For<IDbDataParameter>();
        var instant = new DateTimeOffset(2026, 11, 11, 17, 0, 0, TimeSpan.FromHours(5));

        _handler.SetValue(parameter, instant);

        Assert.Equal(new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc), parameter.Value);
    }
}
