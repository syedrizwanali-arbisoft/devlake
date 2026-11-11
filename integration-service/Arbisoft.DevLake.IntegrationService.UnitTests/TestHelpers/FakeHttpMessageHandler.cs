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

/// <summary>What the fake handler saw: everything a test may want to check about a request, copied out before the request is disposed.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? CorrelationId, string? Content, string? ContentType)
{
    public string AbsoluteUri => Uri.AbsoluteUri;
}

/// <summary>
/// A stand-in for the network. It records each request and answers from a script, one answer per request in order; once the script runs out it
/// repeats the last answer. It sits where the real HTTP handler would, so the resilience pipelines in front of it run for real.
/// </summary>
public sealed class FakeHttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _last;
    private readonly List<RecordedRequest> _requests = [];
    private int _inFlight;
    private int _peakInFlight;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public int PeakConcurrency => _peakInFlight;

    public FakeHttpMessageHandler Respond(HttpStatusCode status, string body = "", string contentType = "application/json")
    {
        return Then((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) }));
    }

    /// <summary>Forgets every scripted answer, so the next one added is the first.</summary>
    public FakeHttpMessageHandler Reset()
    {
        lock (_gate)
        {
            _script.Clear();
            _last = null;
        }

        return this;
    }

    public FakeHttpMessageHandler Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        lock (_gate)
        {
            _script.Enqueue(answer);
        }

        return this;
    }

    public FakeHttpMessageHandler Fail(Exception exception)
    {
        return Then((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    /// <summary>Answers after a delay that honours cancellation, as a slow server would.</summary>
    public FakeHttpMessageHandler RespondSlowly(TimeSpan delay, HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        return Then(async (_, token) =>
        {
            await Task.Delay(delay, token);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
    }

    /// <summary>A handler for an <see cref="HttpClient"/> that forwards to this fake. Each client gets its own, so disposing one leaves the fake usable.</summary>
    public HttpMessageHandler AsHandler() => new Forwarder(this);

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Correlation-Id", out var ids) ? ids.Single() : null,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Content?.Headers.ContentType?.MediaType);

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;
        lock (_gate)
        {
            _requests.Add(recorded);
            answer = _script.Count > 0 ? (_last = _script.Dequeue()) : _last ?? throw new InvalidOperationException("The fake handler has no answer scripted.");
            _peakInFlight = Math.Max(_peakInFlight, ++_inFlight);
        }

        try
        {
            return await answer(request, cancellationToken);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }
    }

    private sealed class Forwarder : HttpMessageHandler
    {
        private readonly FakeHttpMessageHandler _fake;

        public Forwarder(FakeHttpMessageHandler fake)
        {
            _fake = fake;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _fake.HandleAsync(request, cancellationToken);
        }
    }
}
