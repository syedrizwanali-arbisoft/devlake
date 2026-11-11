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
