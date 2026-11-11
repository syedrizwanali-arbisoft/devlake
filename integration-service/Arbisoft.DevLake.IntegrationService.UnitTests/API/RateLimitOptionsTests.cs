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
using Arbisoft.DevLake.IntegrationService.API.RateLimiting;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.API;

public class RateLimitOptionsTests
{
    private static bool IsValid(RateLimitOptions options)
    {
        return Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
    }

    [Fact]
    public void TheDefaults_AreTheLimitsAgreedWithSophia()
    {
        var options = new RateLimitOptions();

        Assert.Equal(100, options.StatsPerMinute);
        Assert.Equal(20, options.BatchPerMinute);
        Assert.Equal(100, options.PollPerMinute);
        Assert.True(IsValid(options));
    }

    [Fact]
    public void ALimitBelowOne_IsRejected()
    {
        Assert.False(IsValid(new RateLimitOptions { StatsPerMinute = 0 }));
        Assert.False(IsValid(new RateLimitOptions { BatchPerMinute = 0 }));
        Assert.False(IsValid(new RateLimitOptions { PollPerMinute = -1 }));
    }
}
