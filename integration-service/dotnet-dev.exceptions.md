<!--
Licensed to the Apache Software Foundation (ASF) under one or more
contributor license agreements.  See the NOTICE file distributed with
this work for additional information regarding copyright ownership.
The ASF licenses this file to You under the Apache License, Version 2.0
(the "License"); you may not use this file except in compliance with
the License.  You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
-->

# Recorded exceptions to the .NET development guidelines

Each row is a deliberate deviation from a rule in the team's .NET guidelines. The rule's intent is applied to what this service uses instead.

| Rule | Scope | Reason |
|---|---|---|
| DAT-1, DAT-4, DAT-5 | Whole service | DEVLAKE-407 and the architecture plan (PR #140) choose Dapper with hand-written SQL and DbUp migrations, not EF Core. The EF-specific rules are applied by intent: versioned SQL scripts, parameterized SQL only, explicit column lists, UTC times, and transactions opened by services. |
| ARC-12 | Whole service | There are only a few small mappings, so they are written by hand next to the DTOs. Revisit with Mapperly if mappings grow. |
| API-2, API-4 | Stats intake endpoints | Sophia's agreed contract returns `{ status, email or emails, correlation_id, error }` with 422 for a request with no callback URL, instead of `ProblemDetails` and 202. All other endpoints follow API-2 and API-4. |
| API-4 | `POST /auth/token/` | The token endpoint returns the RFC 6749 error body (`{ "error": "invalid_client", "error_description": "..." }`) because OAuth client libraries parse that shape. The body still carries no internal detail. |
| API-7 | `GET /fetch/projects` | The endpoint returns the complete project list in one answer, as the ticket requires: DevLake needs every project to tell what exists, and a page of it would be ambiguous. The list is bounded by Workstream's own project count, and the service reads Workstream a page at a time. |
