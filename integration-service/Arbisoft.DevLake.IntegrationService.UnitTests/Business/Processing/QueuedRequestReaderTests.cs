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

using Arbisoft.DevLake.IntegrationService.Business.Services.Processing;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.Business.Processing;

public class QueuedRequestReaderTests
{
    private const string Single = "{\"correlation_id\":\"c1\",\"event_type\":\"engineer-stats\",\"emails\":[\"jane@arbisoft.com\"],\"time_frame\":\"24h\",\"requested_on\":\"2026-11-11T12:00:00+00:00\"}";

    [Fact]
    public void ATimeFrameRequest_IsRead()
    {
        Assert.True(QueuedRequestReader.TryRead(Single, out var readable, out var error));

        Assert.Null(error);
        Assert.Equal("c1", readable!.Request.CorrelationId);
        Assert.Equal(["jane@arbisoft.com"], readable.Request.Emails);
        Assert.Equal("tf=24h", readable.Query.ToQueryString());
    }

    [Fact]
    public void ARangeRequest_IsRead()
    {
        const string json = "{\"correlation_id\":\"c2\",\"event_type\":\"engineer-stats\",\"emails\":[\"a@arbisoft.com\"],\"requested_on\":\"2026-11-11T12:00:00+00:00\",\"from\":\"2026-11-01T00:00:00Z\",\"to\":\"2026-11-08T00:00:00Z\"}";

        Assert.True(QueuedRequestReader.TryRead(json, out var readable, out _));

        Assert.Equal("from=2026-11-01T00%3A00%3A00Z&to=2026-11-08T00%3A00%3A00Z", readable!.Query.ToQueryString());
    }

    [Fact]
    public void ABatch_KeepsEveryEmail()
    {
        const string json = "{\"correlation_id\":\"c3\",\"event_type\":\"engineer-stats-batch\",\"emails\":[\"a@arbisoft.com\",\"b@arbisoft.com\"],\"time_frame\":\"1w\",\"requested_on\":\"2026-11-11T12:00:00+00:00\"}";

        Assert.True(QueuedRequestReader.TryRead(json, out var readable, out _));

        Assert.Equal(2, readable!.Request.Emails.Count);
    }

    [Fact]
    public void FieldsItDoesNotKnow_AreIgnored_SoANewerApiCanAddFields()
    {
        const string json = "{\"correlation_id\":\"c4\",\"event_type\":\"engineer-stats\",\"emails\":[\"a@arbisoft.com\"],\"time_frame\":\"24h\",\"requested_on\":\"2026-11-11T12:00:00+00:00\",\"priority\":\"high\",\"nested\":{\"x\":[1,2,3]}}";

        Assert.True(QueuedRequestReader.TryRead(json, out var readable, out _));

        Assert.Equal("c4", readable!.Request.CorrelationId);
    }

    [Theory]
    [InlineData("not json at all", "not valid JSON")]
    [InlineData("", "not valid JSON")]
    [InlineData("null", "no emails")]
    [InlineData("{}", "no emails")]
    [InlineData("{\"emails\":[],\"time_frame\":\"24h\"}", "no emails")]
    [InlineData("{\"emails\":[\"\"],\"time_frame\":\"24h\"}", "no emails")]
    [InlineData("{\"emails\":[\"  \"],\"time_frame\":\"24h\"}", "no emails")]
    [InlineData("{\"emails\":[\"a@arbisoft.com\"]}", "Provide tf")]
    [InlineData("{\"emails\":[\"a@arbisoft.com\"],\"time_frame\":\"24H\"}", "case-sensitive")]
    [InlineData("{\"emails\":[\"a@arbisoft.com\"],\"time_frame\":\"24h\",\"from\":\"2026-11-01T00:00:00Z\",\"to\":\"2026-11-02T00:00:00Z\"}", "not both")]
    [InlineData("{\"emails\":[\"a@arbisoft.com\"],\"from\":\"2026-11-02T00:00:00Z\",\"to\":\"2026-11-01T00:00:00Z\"}", "earlier")]
    [InlineData("{\"emails\":[\"a@arbisoft.com\"],\"from\":\"yesterday\",\"to\":\"today\"}", "ISO 8601")]
    public void ABodyItCannotUse_IsReportedWithAReason_NeverGuessedAt(string json, string expectedReason)
    {
        Assert.False(QueuedRequestReader.TryRead(json, out var readable, out var error));

        Assert.Null(readable);
        Assert.Contains(expectedReason, error, StringComparison.Ordinal);
    }
}
