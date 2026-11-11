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

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>What an API call returned: the status, the headers, and the body as text and, when it has one, as JSON.</summary>
public sealed record ApiResponse(HttpStatusCode Status, HttpResponseHeaders Headers, string Text)
{
    public JsonElement Json => JsonDocument.Parse(Text).RootElement;

    public string Correlation => Json.GetProperty("correlation_id").GetString()!;
}

/// <summary>Hosts the real API in-process on a fresh signing key for each test, with helpers to call it as a client would.</summary>
public abstract class ApiTestBase : IAsyncLifetime, IDisposable
{
    private readonly MySqlDatabaseFixture _database;
    private readonly IReadOnlyDictionary<string, string>? _settings;

    protected ApiTestBase(MySqlDatabaseFixture database, IReadOnlyDictionary<string, string>? settings = null)
    {
        _database = database;
        _settings = settings;
    }

    /// <summary>The network the API's calls to DevLake, Sophia and Workstream go to. Nothing is scripted by default.</summary>
    protected FakeHttpMessageHandler Upstream { get; } = new();

    protected ApiFactory Factory { get; private set; } = null!;

    protected HttpClient Http { get; private set; } = null!;

    protected ApiSeed Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (_database.Services is null)
        {
            return;
        }

        Factory = new ApiFactory(_database.DatabaseConnectionString, _settings, Upstream);
        Http = Factory.CreateClient();
        Seed = new ApiSeed(Factory);
        await Seed.SeedSigningKeyAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        Http?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected static string Encode(string email) => Uri.EscapeDataString(email);

    /// <summary>A client that may request stats and has a callback URL for each given event type.</summary>
    protected async Task<(SeededClient Client, string Token)> ClientWithCallbacksAsync(params string[] eventTypes)
    {
        var client = await Seed.SeedClientAsync();
        if (eventTypes.Length > 0)
        {
            await Seed.SeedCallbackAsync(client, eventTypes);
        }

        return (client, await ApiSeed.RequestTokenAsync(Http, client));
    }

    protected Task<ApiResponse> GetStatsAsync(string token, string email, string? timeFrame = "24h")
    {
        var query = timeFrame is null ? string.Empty : $"?tf={Uri.EscapeDataString(timeFrame)}";
        return SendAsync(HttpMethod.Get, $"/api/v1/engineers/{Encode(email)}/stats{query}", token);
    }

    protected Task<ApiResponse> PostBatchAsync(string token, object? body)
    {
        return SendAsync(HttpMethod.Post, "/api/v1/engineers/stats/batch", token, body is null ? null : JsonSerializer.Serialize(body), "application/json");
    }

    protected async Task<ApiResponse> SendAsync(HttpMethod method, string path, string? token, string? content = null, string? contentType = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, contentType ?? "application/json");
        }

        using var response = await Http.SendAsync(request);
        return new ApiResponse(response.StatusCode, response.Headers, await response.Content.ReadAsStringAsync());
    }

    protected static string[] Emails(int count) => Enumerable.Range(1, count).Select(i => $"user{i}@arbisoft.com").ToArray();
}
