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

Connects DevLake with third-party systems, so that nothing outside DevLake calls it directly. This is the implementation of US-39. The design is in `integration-service-architecture-plan.md` (PR #140).

## Layout

```
API, Web  -> Business -> DataAccess
```

API and Web can't compile against DataAccess types, because transitive project references are turned off.

## Run locally

Requires the .NET 10 SDK and Docker.

```sh
cp env.example .env   # then set the passwords
docker compose up -d --wait

export ConnectionStrings__IntegrationDb='Server=127.0.0.1;Port=3308;Database=integration;User ID=integration;Password=<INTEGRATION_DB_PASSWORD>'
dotnet run --project Arbisoft.DevLake.IntegrationService.API
```

The API applies pending migrations at startup. A failed script stops startup.

## Stats APIs

| API | Rate limit per client |
|---|---|
| `GET /api/v1/engineers/{email}/stats?tf=24h` | `RateLimits:StatsPerMinute` (100) |
| `POST /api/v1/engineers/stats/batch` | `RateLimits:BatchStatsPerMinute` (20) |
| `GET /api/v1/requests/poll/{uuid}` | `RateLimits:PollPerMinute` (100) |

All three need a token with the `stats:read` scope. The stats APIs answer straight away with a `correlation_id`, and the poll API returns that request's current status: `Queued`, `Processing`, `Success`, `Failure` or `Revoked`. A client can only poll its own requests; for anyone else's, the poll API returns 404. A request is queued only if the client has an active `ClientConfigOutboundUrls` row for its event type: `engineer-stats` for the single API and `engineer-stats-batch` for the batch API. Without one, the request is logged as `Revoked - no callback URL` and gets a 422.

The same client sending the same emails and time frame within `Requests:DuplicateWindowSeconds` (300) gets the original `correlation_id`, and nothing new is queued. Revoked requests don't count as duplicates.

## Processing job

The API runs `RequestProcessingJob` at startup and every `Processing:IntervalSeconds` (60); the Web app has it off. Each run checks the DevLake token, then takes up to `Processing:MaxMessagesPerRun` requests from the queue. For each request it:

1. checks the callback again, and marks the request `Revoked - no callback URL` if it's gone,
2. checks the client's token,
3. sets `Processing` and fetches Jira, GitHub, AI usage and AI cost stats in parallel for the engineer's Arbisoft email and every mapped email (at most `Processing:MaxParallelDevLakeCalls` at once),
4. sets `Processed`, posts `{ correlation_id, time_frame, engineers: [...] }` to the callback up to `maxAttempts` times (waiting `DeliveryRetryDelaySeconds` × attempt between tries) and records each attempt,
5. sets `Success`, or `Failed` once every attempt fails.

Problems that affect every request stop the run and leave the request `Queued` for the next run: a rejected token, or DevLake down (5xx, 408, 429 or no response). A problem with only one request, such as a 4xx from DevLake or an incomplete client config, marks that request `Failed`, and the run carries on.

## Outbound clients

| Client | Settings | Calls |
|---|---|---|
| DevLake | `DevLake:BaseUrl`, `DevLake:TimeoutSeconds` | `GET is_valid`, `GET stats/{email}/{jira,github,ai_usage,ai_cost}?tf=` |
| Workstream | `Workstream:BaseUrl`, `Workstream:PageSize`, `Workstream:TimeoutSeconds` | `GET is_valid`, `GET projects?page=&pageSize=` until the last page |
| Sophia | `ClientConfigs` and `ClientConfigOutboundUrls` rows; `Sophia:TimeoutSeconds` | OAuth2 token request, then the callback |

Every client has a total timeout and retries transient failures (5xx, 408, 429 and network errors) with backoff. Sophia's callback is sent once with no HTTP retry, because the processing job records each attempt and retries up to `maxAttempts`. A client config's `authType` is `oauth2` (client credentials) or `api_token` (a static bearer token), and its secrets are stored encrypted with `Security:EncryptionKey`.

The response shapes of DevLake's stats endpoints are defined with them in DEVLAKE-418, so for now the client returns their JSON as is. Workstream's API isn't documented yet. The client accepts each page either as a JSON array, which ends with a short page, or as `{ "results": [...], "next": ... }`, which ends when `next` is null.

## Request queue

Stats requests go to the durable queue `RabbitMq:RequestQueue` as persistent messages. The processing job takes them in batches and acknowledges each one only after it is handled. A message that fails stays queued for the next run. If it fails again, it moves to `<RequestQueue>.failed`, where it stays for inspection or replay.

## Configuration

Secrets come from environment variables, never from `appsettings.json`.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__IntegrationDb` | MySQL connection string |
| `Security__EncryptionKey` | Base64 of a 32-byte AES key, for stored outbound credentials |
| `RabbitMq__Username`, `RabbitMq__Password` | RabbitMQ credentials, the same values as `RABBITMQ_USER` and `RABBITMQ_PASSWORD` in `.env` |
| `DevLake__ApiToken` | DevLake's API token, sent as `Authorization: Bearer` |
| `Workstream__ApiToken` | Workstream's API token |
| `Secrets__<privateKeyRef>` | PEM private key of the signing key whose `privateKeyRef` is `<privateKeyRef>` |

## Test

```sh
dotnet test
```
