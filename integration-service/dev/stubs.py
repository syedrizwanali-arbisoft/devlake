# Licensed to the Apache Software Foundation (ASF) under one or more
# contributor license agreements.  See the NOTICE file distributed with
# this work for additional information regarding copyright ownership.
# The ASF licenses this file to You under the Apache License, Version 2.0
# (the "License"); you may not use this file except in compliance with
# the License.  You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""Local stand-ins for Workstream and Sophia, for trying the Integration Service end to end. Not for production.

Workstream (under /ws): GET /ws/is_valid and paged GET /ws/projects, with the bearer token WORKSTREAM_TOKEN.
Sophia (under /sophia): POST /sophia/oauth2/token/ and POST /sophia/api/v1/devlake/callback/.
GET /sophia/callbacks lists every callback received, newest first.
STUB_LINKED_PROJECTS (names separated by |) names the first sample projects after existing DevLake projects, to show linking.
"""
import base64, http.server, json, os, sys
from urllib.parse import urlparse, parse_qs

WORKSTREAM_TOKEN = os.environ["WORKSTREAM_TOKEN"]
SOPHIA_CLIENT = os.environ["SOPHIA_OUTBOUND_CLIENT_ID"] + ":" + os.environ["SOPHIA_OUTBOUND_CLIENT_SECRET"]
LINKED = [n for n in os.environ.get("STUB_LINKED_PROJECTS", "").split("|") if n]
PROJECTS = [
    {"id": 101, "name": LINKED[0] if LINKED else "Sample Project A", "team_lead": {"name": "Sample Lead One", "email": "Sample.Lead.One@arbisoft.com"}},
    {"id": 102, "name": LINKED[1] if len(LINKED) > 1 else "Sample Project B", "team_lead": {"name": "Sample Lead Two", "email": "sample.lead.two@arbisoft.com"}},
    {"id": 103, "name": "Workstream Only Project", "team_lead": {"name": "Sample Lead Three", "email": "sample.lead.three@arbisoft.com"}},
    {"id": 104, "name": "Project Without Lead", "team_lead": None},
    {"id": 105, "name": "Another Workstream Project", "team_lead": {"name": "Sample Lead Four", "email": "sample.lead.four@arbisoft.com"}},
]
callbacks = []


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        sys.stderr.write("stub %s\n" % (fmt % args))

    def _send(self, code, body):
        data = json.dumps(body, indent=2).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _bearer_ok(self):
        return self.headers.get("Authorization") == "Bearer " + WORKSTREAM_TOKEN

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/ws/is_valid":
            return self._send(200 if self._bearer_ok() else 401, self._bearer_ok())
        if url.path == "/ws/projects":
            if not self._bearer_ok():
                return self._send(401, {"detail": "invalid token"})
            q = parse_qs(url.query)
            page, size = int(q.get("page", ["1"])[0]), int(q.get("pageSize", ["10"])[0])
            items = PROJECTS[(page - 1) * size: page * size]
            more = page * size < len(PROJECTS)
            return self._send(200, {"count": len(PROJECTS), "next": "page=%d" % (page + 1) if more else None, "results": items})
        if url.path == "/sophia/callbacks":
            return self._send(200, list(reversed(callbacks)))
        self._send(404, {"detail": "not found"})

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        if self.path == "/sophia/oauth2/token/":
            auth = self.headers.get("Authorization", "")
            ok = auth.startswith("Basic ") and base64.b64decode(auth[6:]).decode() == SOPHIA_CLIENT
            return self._send(200, {"access_token": "sophia-local-token", "token_type": "Bearer", "expires_in": 3600}) if ok else self._send(401, {"error": "invalid_client"})
        if self.path == "/sophia/api/v1/devlake/callback/":
            if self.headers.get("Authorization") != "Bearer sophia-local-token":
                return self._send(401, {"error": "invalid_token"})
            callbacks.append({"correlation_id": self.headers.get("X-Correlation-ID"), "body": json.loads(body)})
            return self._send(200, {"received": True})
        self._send(404, {"detail": "not found"})


http.server.ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("STUB_PORT", "5097"))), Handler).serve_forever()
