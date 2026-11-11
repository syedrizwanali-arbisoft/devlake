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

# Integration Service

A .NET 10 service that sits between DevLake and third-party systems such as Sophia and Workstream. The design is in `integration-service-architecture-plan.md` (PR #140).

## Layout

Each layer references only the layer directly below it.

| Project | Role |
|---|---|
| `Arbisoft.DevLake.IntegrationService.API` | Controllers, authorization, rate limiting, background jobs |
| `Arbisoft.DevLake.IntegrationService.Web` | Razor Pages admin portal |
| `Arbisoft.DevLake.IntegrationService.Business` | Services, DTOs, token and encryption logic |
| `Arbisoft.DevLake.IntegrationService.DataAccess` | Models, Dapper repositories, SQL migrations (DbUp), external API clients, RabbitMQ |
| `Arbisoft.DevLake.IntegrationService.UnitTests` | xUnit tests |

Deviations from the team's .NET guidelines are recorded in `dotnet-dev.exceptions.md`.

## Run locally

Start MySQL (port 3308) and RabbitMQ (ports 5672 and 15672):

```sh
cp env.example .env   # then edit the values
docker compose up -d
```

Settings live in each project's `appsettings.json`. Secrets are never put there. Set them as environment variables, using `__` for nesting:

```sh
export Database__ConnectionString="Server=127.0.0.1;Port=3308;Database=integration;User=integration;Password=<password>"
export RabbitMq__UserName=<user> RabbitMq__Password=<password>
export DevLake__ApiToken=<token> Workstream__ApiToken=<token>
export Security__EncryptionKey=$(openssl rand -base64 32)   # keep it: it decrypts the stored secrets
dotnet run --project Arbisoft.DevLake.IntegrationService.API
```

The host refuses to start if a required setting is missing or `Security__EncryptionKey` is not 32 bytes of base64.

Private signing keys are stored AES-256-GCM encrypted in the `SecretStoreEntries` table, named by `SigningKeys.privateKeyRef`. An environment variable `Secrets__<privateKeyRef>` overrides the stored copy, so a deployment can keep a key in a vault instead. If the master key is lost, rotate to a new signing key: tokens signed with the old one still verify because public keys are in the database.

Health endpoints: `/health/live` and `/health/ready`. Readiness checks MySQL and RabbitMQ.

## Request queue

Stats requests wait in a durable RabbitMQ quorum queue (`RabbitMq:RequestQueueName`). Messages are persistent, carry the correlation ID and a `schema-version` header, and are confirmed by the broker before the API answers.

| Setting | Default | Effect |
|---|---|---|
| `QueueMaxLength` | 10000 | When the queue is full the broker rejects the publish and the API answers 429 |
| `MessageTtlSeconds` | 7200 | A request that waits longer expires into the dead-letter queue |
| `DeliveryLimit` | 5 | A message redelivered more often than this is moved to the dead-letter queue by the consumer |
| `ReceiveBatchLimit` | 10 | The most messages one processing run takes from the queue |

Failed, expired and unreadable messages go to `<queue>.dead` through the `<queue>.dlx` exchange. Inspect them in the RabbitMQ management UI (http://localhost:15672). To replay one, use the UI's "Move messages" shovel from the dead-letter queue back to the request queue.

## Stats requests

`GET /api/v1/engineers/{email}/stats?tf=24h` and `POST /api/v1/engineers/stats/batch` (body `{ "emails": [...], "timeFrame": "24h" }`, up to 20 emails) need the `stats:read` scope. Both are asynchronous: they log the request, queue it and return a `correlation_id` at once, and the result is posted later to the client's callback URL.

- **Time frame:** a whole number and a case-sensitive unit: `h`, `d`, `w`, `M` (30 days) or `y` (365 days), for example `24h`, `1w`, `30d`, `11M`, `1y`. The longest is one year.
- **Date range:** instead of `tf`, the single-email API takes `from` and `to`, for example `GET /api/v1/engineers/{email}/stats?from=2026-11-01T00:00:00Z&to=2026-11-08T00:00:00Z`. Give a time frame or a range, never both, and always both ends of a range. Timestamps are ISO 8601 with a time and an explicit offset (`Z` or `+05:00`; write the `+` as `%2B` in a URL). A date with no time or offset is rejected because it would depend on a time zone. They are converted to UTC at second precision, `from` must be earlier than `to`, and the range can be at most one year. The queued request carries `from` and `to` instead of `time_frame`.
- **Emails** are trimmed and lower-cased, and must be in an allowed domain. Raw emails are never written to logs or error responses.
- **No callback URL:** when the client has no active outbound URL for the event type (`engineer-stats` or `engineer-stats-batch`), the request is logged as `Revoked - no callback URL`, nothing is queued, and the API answers 422 with that reason in `error`.
- **Duplicates:** an identical request (same client, emails and time frame) inside the duplicate window returns the original acknowledgement. A unique key in the database is what prevents the duplicate, so concurrent identical requests also produce one. Revoked and failed requests never block a repeat.
- **Queue problems:** 429 with `Retry-After` when the queue is full, 503 with `Retry-After` when the broker is unreachable. Nothing is recorded in either case.

## Processing requests

A background job in the API takes queued stats requests off RabbitMQ, collects the stats from DevLake, and delivers them to the client's callback. It is on by default and can be turned off with `Processing:Enabled=false` (requests then stay queued).

Every `Processing:IntervalSeconds` (30) a run does this, in order:

1. Marks requests that were never finished, because their message expired from the queue or was dead-lettered, as `Failed`, so polling does not say `Queued` forever.
2. Checks the queue depth, and does nothing more if it is empty.
3. Checks that DevLake accepts our API token and that a Sophia access token can be obtained for every client that has requests waiting. If not, the run stops **before taking any message**, so requests stay queued and no message's redelivery count goes up. A client with nothing waiting is not checked.
4. Takes up to `RabbitMq:ReceiveBatchLimit` (10) messages and processes up to `Processing:MaxParallelRequests` (4) at a time.

For each request the job:

- skips and acknowledges it if it is already finished (a repeat delivery does no second delivery to the client);
- marks it `Revoked - no callback URL` if the callback was removed while it waited;
- sets it to `Processing`, loads the engineer's mapped emails, and fetches Jira, GitHub, AI usage and AI cost for the Arbisoft email and every mapped email at once;
- posts one merged body to the callback, up to the client's `maxAttempts` times, waiting `Processing:DeliveryRetryDelaySeconds` (30) after the first failure and twice as long after each further one, and records every attempt in `RequestResponseAttempts`;
- ends the request `Success` or `Failed`, then acknowledges the message.

One failing request never stops the others. DevLake or Sophia being down, or refusing our credentials, is systemic: the request goes back to `Queued`, the rest of the run is not started, and the next run tries again. A request DevLake refuses (a 4xx) fails on its own with the reason recorded.

The callback body (Sophia has not specified one):

```json
{
  "correlation_id": "0b8f3c52-...",
  "time_frame": "24h",
  "engineers": [
    {
      "email": "jane@arbisoft.com",
      "mapped_emails": ["jane.d@gmail.com"],
      "jira":     { "jane@arbisoft.com": { ... } },
      "github":   { "jane@arbisoft.com": { ... }, "jane.d@gmail.com": { ... } },
      "ai_usage": { "jane@arbisoft.com": { ... } },
      "ai_cost":  { "jane@arbisoft.com": { ... } }
    }
  ]
}
```

A range request has `from` and `to` instead of `time_frame`. Each source holds the stats of every email as DevLake returned them. A batch has one entry per engineer.

On shutdown the job stops taking new work. A delivery to Sophia that has already started is finished and recorded, so a delivered result is never left unrecorded; everything else goes back on the queue.

A request that waits longer than the queue's message lifetime plus `Processing:StaleGraceSeconds` (60) is marked `Failed` with an `Expired` attempt.

| Setting | Default | Effect |
|---|---|---|
| `Processing:Enabled` | true | Turns the job on or off |
| `Processing:IntervalSeconds` | 30 | How often the job runs |
| `Processing:MaxParallelRequests` | 4 | Requests processed at the same time in one run |
| `Processing:DeliveryRetryDelaySeconds` | 30 | Wait before the second delivery attempt; doubles each time |
| `Processing:StaleGraceSeconds` | 60 | Extra time past the queue lifetime before an unfinished request is failed |

## Calls to DevLake, Sophia and Workstream

Three typed HTTP clients make the outbound calls. Each has its own timeout and circuit breaker, and none of them logs a URL, a token or a payload, only the correlation ID, the system and the status.

| Client | Calls | Authentication |
|---|---|---|
| DevLake | `GET /is_valid`, `GET /stats/{email}/jira`, `/github`, `/ai_usage`, `/ai_cost` with `?tf=24h` or `?from=...&to=...`. `DevLake:BaseUrl` ends in `/rest/` | `Authorization: Bearer <DevLake:ApiToken>` |
| Workstream | `GET /is_valid`, `GET /projects?page=N&pageSize=M` | `Authorization: Bearer <Workstream:ApiToken>` |
| Sophia | `POST /oauth2/token/`, then the callback (`POST /api/v1/devlake/callback/`) | OAuth2 client credentials, or a stored API token |

Sophia's base URL, token URL, client ID, encrypted secret, scope and callback path and method come from the client's `ClientConfigs` and `ClientConfigOutboundUrls` rows, because each environment (Stage, Prod) has its own. The access token is reused until a minute before it expires, a rejected token is replaced once, and URLs must use HTTPS (plain HTTP only for loopback, for local stubs).

### DevLake endpoints

DevLake serves these for the Integration Service (`backend/server/api/integration`). They are reached through `/rest/...`. There, DevLake checks `Authorization: Bearer <API key>` and the key's allowed path, so create a DevLake API key whose allowed path is `^/(is_valid|stats/.+)$`. A direct call without the `/rest` prefix gets 401, even when login is off.

- **`GET is_valid`:** `true`. An invalid, expired or out-of-scope key gets 403 from DevLake, which the client reports as `false`.
- **Period:** give `tf` (1 to 999 and one of `h`, `d`, `w`, `M`, `y`, case-sensitive) or both `from` and `to` (RFC 3339 with an explicit offset, `from` earlier than `to`, at most one year). Giving both forms, one end of a range, or nothing gets 400 with a message naming the field.
- **Every stats response** starts with `email` (lowercased), `timeFrame` (left out for a range), and `from` and `to` (RFC 3339 UTC, `[from, to)`). An email with no data gets zeros and empty lists.

| Category | Fields after the common ones |
|---|---|
| `jira` | `issuesResolved`, `storyPointsResolved`, `issuesCreated`, and the current `issuesInProgress` and `issuesTodo` |
| `github` | `commits`, `additions`, `deletions`, `pullRequestsOpened`, `pullRequestsMerged`, `pullRequestsWithCommits`, `reviews` |
| `ai_usage` | `providers: [{ provider, activeDays, sessions, suggestions, acceptances, linesAdded, linesRemoved, commitsByAi, pullRequests }]`, `toolEditsAccepted`, `toolEditsRejected` |
| `ai_cost` | `models: [{ provider, model, inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, estimatedCostUsd }]`, `totalEstimatedCostUsd` (decimal strings in USD) |

Data limits:

- **Jira and GitHub accounts:** Jira stats, and GitHub pull request authorship and reviews, match on the account's email, which many accounts hide. Commit counts and `pullRequestsWithCommits` use the commit author email, which is reliable.
- **AI data:** it is daily, so AI windows cover whole UTC days.

Every failure becomes one of four exceptions, so callers handle outcomes, not HTTP details:

| Exception | Meaning | What a caller should do |
|---|---|---|
| `ExternalAuthenticationException` | Credentials refused, or no usable token | Stop and leave work queued until the credentials are fixed |
| `ExternalApiUnavailableException` | Timeout, connection error, 5xx or 429 after the retries, or an open circuit | Try again later |
| `ExternalApiRejectedException` | The system refused this request (other 4xx) | Fail this request only |
| `ExternalApiResponseException` | A success answer that is not what the contract says | Fail this request only |

Only read calls retry (timeouts, 5xx and 429, never a 4xx). The Sophia callback is not retried by the HTTP layer: it returns the answer, successful or not, so the processing job can retry up to `maxAttempts` and record every attempt.

| Setting | Default | Effect |
|---|---|---|
| `DevLake:TimeoutSeconds`, `Sophia:TimeoutSeconds`, `Workstream:TimeoutSeconds` | 30 | How long one attempt waits |
| `DevLake:MaxConcurrentRequests` | 8 | Most requests in flight to DevLake at once, across the process |
| `Resilience:MaxRetries` | 3 | Retries of a failed read call |
| `Resilience:RetryBaseDelayMilliseconds` | 1000 | First retry delay; it doubles each time, with jitter |
| `Resilience:BreakerFailureRatio` | 0.5 | Share of failed calls that opens the circuit |
| `Resilience:BreakerMinimumThroughput` | 10 | Calls needed in the sampling window before it can open |
| `Resilience:BreakerSamplingSeconds` | 30 | The sampling window |
| `Resilience:BreakerBreakSeconds` | 30 | How long the circuit stays open before one probe call |

## Projects from Workstream

`GET /fetch/projects` (scope `projects:fetch`, not versioned) returns every Workstream project with its team lead, so DevLake never calls Workstream itself. It first checks Workstream's token, then reads `/projects?page=N&pageSize=M` page after page (`Workstream:PageSize`, default 10) until a short page, a page with nothing new on it, or a safety limit of 10,000 pages.

```json
[ { "id": "p-1", "name": "Apollo", "team_lead": { "name": "Jane Doe", "email": "jane.doe@arbisoft.com" } } ]
```

- Only `id`, `name` and `team_lead` (`name` and `email`, either may be null) leave the service, whatever else Workstream sends. `team_lead` is null when Workstream has none.
- The list is complete or it is an error. A failure on any page returns an error and no projects, because a partial list would make DevLake think projects had been removed. A project listed on two pages is returned once.
- **Workstream's response shape is not documented**, so the mapper accepts the usual names: a bare array or an object holding the list under `results`, `data`, `items` or `projects`; `id`, `project_id`, `projectId` or `uuid`; `name`, `title`, `project_name` or `projectName`; and a team lead as an object under `team_lead`, `teamLead`, `lead`, `manager` or `project_manager` (or as flat `team_lead_name` and `team_lead_email` fields). A project with no id or no name fails the whole call. Once Workstream's real shape is known, `ProjectMapper` is the one place to change.

### Loading projects into DevLake

DevLake calls this from its **Project Ingestion** screen (admins only), through `POST /projects/load`. To set it up:

1. In the admin portal, create a client for DevLake with the `projects:fetch` scope, and issue it a secret.
2. Set `INTEGRATION_SERVICE_URL`, `INTEGRATION_SERVICE_CLIENT_ID` and `INTEGRATION_SERVICE_CLIENT_SECRET` in DevLake's `.env`. They stay on the server and never reach the browser. With any of the three empty, project ingestion is off. `INTEGRATION_SERVICE_TIMEOUT_SECONDS` (default 60) bounds one load.

DevLake gets a token from `POST /auth/token/`, calls `GET /fetch/projects`, and saves each project and its team lead in `workstream_projects`, keyed by Workstream ID, so loading again updates rows instead of duplicating them. `GET /workstream-projects` lists them and links each to the DevLake project of the same name. A load never creates DevLake projects.

Errors (all `ProblemDetails` with a `code` and the correlation ID, and never any Workstream data):

| Status | `code` | When |
|---|---|---|
| 502 | `workstream_token_invalid` | Workstream rejected this service's API token |
| 502 | `upstream_credentials` | Workstream rejected the token part way through |
| 502 | `upstream_rejected` | Workstream refused a page request |
| 502 | `upstream_bad_response` | The answer was not a list of readable projects |
| 503 (with `Retry-After`) | `upstream_unavailable` | Workstream could not be reached, or kept failing after the retries |

## Request status

`GET /api/v1/requests/poll/{uuid}` (scope `stats:read`) takes the `correlation_id` from a stats response and answers at once with `{ "correlation_id", "status", "check_time" }`. `check_time` is the time of the call in UTC, for example `2026-11-05T14:02:55Z`. The status is one of these, spelled exactly like this:

| Request log status | Poll status |
|---|---|
| Received, Queued | `Queued` |
| Processing, Processed | `Processing` |
| Success | `Success` |
| Failed | `Failure` |
| Revoked - no callback URL | `Revoked` |

A client can only read its own requests. An unknown ID and another client's ID both return the same 404, and a malformed ID returns 400. The poll API has its own rate limit (`RateLimits:PollPerMinute`).

These settings can be changed without code changes:

| Setting | Default | Effect |
|---|---|---|
| `Requests:DuplicateWindowMinutes` | 15 | How long an identical request counts as a duplicate. Read on every request, so editing `appsettings.json` applies without a restart |
| `Requests:AllowedEmailDomains` | `["arbisoft.com"]` | Domains whose emails are accepted |
| `RateLimits:StatsPerMinute` | 100 | Single-email requests per client per minute |
| `RateLimits:BatchPerMinute` | 20 | Batch requests per client per minute |
| `RateLimits:PollPerMinute` | 100 | Status poll requests per client per minute |

As with every setting, an environment variable overrides the file, for example `Requests__DuplicateWindowMinutes=5`.

## Build and test

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```

Tests that need a database or broker are skipped unless these are set (the compose stack provides both):

```sh
export INTEGRATION_TEST_MYSQL="Server=127.0.0.1;Port=3308;User=root;Password=<root password>"
export INTEGRATION_TEST_RABBITMQ="host=127.0.0.1;port=5672;management=15672;user=<user>;password=<password>"
```

Each test creates its own throwaway database or queue and removes it afterwards.

CI runs the same steps plus a vulnerable-package check (`.github/workflows/integration-service.yml`).
