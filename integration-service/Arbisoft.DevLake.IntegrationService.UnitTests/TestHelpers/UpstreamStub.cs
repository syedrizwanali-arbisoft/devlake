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
using System.Text;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

/// <summary>
/// A scripted DevLake and Sophia behind a <see cref="FakeHttpMessageHandler"/>. A test sets how each should behave, and reads back what the service
/// sent. DevLake is recognised by its path (<c>/is_valid</c>, <c>/stats/...</c>) and Sophia by its host.
/// </summary>
public sealed class UpstreamStub
{
    public const string SophiaHost = "sophia.test";

    /// <summary>The ports the API under test is configured to reach DevLake and Workstream on, so a request can be told apart by where it went.</summary>
    public const int DevLakePort = 8080;
    public const int WorkstreamPort = 5097;

    private readonly object _gate = new();
    private readonly FakeHttpMessageHandler _handler;
    private readonly Queue<HttpStatusCode> _callbackScript = new();
    private int _tokensIssued;

    public UpstreamStub(FakeHttpMessageHandler handler)
    {
        _handler = handler;
        handler.Then((request, _) => Task.FromResult(Answer(request)));
    }

    /// <summary>Whether DevLake accepts our API token.</summary>
    public bool DevLakeTokenValid { get; set; } = true;

    /// <summary>The status DevLake answers with for each email's stats.</summary>
    public Func<string, HttpStatusCode> DevLakeStats { get; set; } = _ => HttpStatusCode.OK;

    /// <summary>Whether Workstream accepts our token.</summary>
    public bool WorkstreamTokenValid { get; set; } = true;

    /// <summary>What Workstream answers for each page of projects: a status and a JSON body.</summary>
    public Func<int, (HttpStatusCode Status, string Body)> WorkstreamPages { get; set; } = _ => (HttpStatusCode.OK, "[]");

    /// <summary>The status Sophia's token endpoint answers with.</summary>
    public HttpStatusCode SophiaTokenStatus { get; set; } = HttpStatusCode.OK;

    public IEnumerable<RecordedRequest> WorkstreamProjectRequests => _handler.Requests.Where(r => r.Uri.Port == WorkstreamPort && r.Uri.AbsolutePath == "/projects");

    public IEnumerable<RecordedRequest> WorkstreamTokenChecks => _handler.Requests.Where(r => r.Uri.Port == WorkstreamPort && r.Uri.AbsolutePath == "/is_valid");

    public IEnumerable<RecordedRequest> DevLakeStatsRequests => _handler.Requests.Where(r => r.Uri.AbsolutePath.StartsWith("/stats/", StringComparison.Ordinal));

    public IEnumerable<RecordedRequest> DevLakeTokenChecks => _handler.Requests.Where(r => r.Uri.AbsolutePath == "/is_valid");

    public IEnumerable<RecordedRequest> SophiaTokenRequests => _handler.Requests.Where(r => r.Uri.Host == SophiaHost && r.Uri.AbsolutePath == "/oauth2/token/");

    public IEnumerable<RecordedRequest> Callbacks => _handler.Requests.Where(r => r.Uri.Host == SophiaHost && r.Uri.AbsolutePath != "/oauth2/token/");

    /// <summary>The statuses Sophia's callback answers with, one per call. After the script runs out it answers 200.</summary>
    public void ScriptCallbacks(params HttpStatusCode[] statuses)
    {
        lock (_gate)
        {
            _callbackScript.Clear();
            foreach (var status in statuses)
            {
                _callbackScript.Enqueue(status);
            }
        }
    }

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        if (uri.Host == SophiaHost)
        {
            return uri.AbsolutePath == "/oauth2/token/" ? SophiaToken() : SophiaCallback();
        }

        if (uri.Port == WorkstreamPort)
        {
            return Workstream(uri);
        }

        if (uri.AbsolutePath == "/is_valid")
        {
            return Json(DevLakeTokenValid ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, DevLakeTokenValid ? "true" : "{}");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var email = Uri.UnescapeDataString(segments[1]);
        var status = DevLakeStats(email);
        return Json(status, status == HttpStatusCode.OK ? $"{{\"source\":\"{segments[2]}\",\"for\":\"{email}\"}}" : "{\"error\":\"nope\"}");
    }

    private HttpResponseMessage Workstream(Uri uri)
    {
        if (uri.AbsolutePath == "/is_valid")
        {
            return Json(WorkstreamTokenValid ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, WorkstreamTokenValid ? "true" : "{}");
        }

        var page = int.Parse(System.Web.HttpUtility.ParseQueryString(uri.Query)["page"]!, System.Globalization.CultureInfo.InvariantCulture);
        var (status, body) = WorkstreamPages(page);
        return Json(status, body);
    }

    private HttpResponseMessage SophiaToken()
    {
        if (SophiaTokenStatus != HttpStatusCode.OK)
        {
            return Json(SophiaTokenStatus, "{\"error\":\"invalid_client\"}");
        }

        var number = Interlocked.Increment(ref _tokensIssued);
        return Json(HttpStatusCode.OK, $"{{\"access_token\":\"tok-{number}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}");
    }

    private HttpResponseMessage SophiaCallback()
    {
        HttpStatusCode status;
        lock (_gate)
        {
            status = _callbackScript.Count > 0 ? _callbackScript.Dequeue() : HttpStatusCode.OK;
        }

        return Json(status, status == HttpStatusCode.OK ? "{\"received\":true}" : "{\"error\":\"sophia says no\"}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
