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

using Arbisoft.DevLake.IntegrationService.API.Authorization;
using Arbisoft.DevLake.IntegrationService.API.RateLimiting;
using Arbisoft.DevLake.IntegrationService.Business;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBusiness(builder.Configuration);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    // Keep the error shape of the Sophia contract when a body can't be read, instead of ProblemDetails.
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(
        new { status = "400", correlation_id = string.Empty, error = "The request body is not valid." });
});
builder.Services.AddApiSecurity();
builder.Services.AddApiRateLimiting(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

// Lets the tests start the real pipeline through WebApplicationFactory.
public partial class Program;
