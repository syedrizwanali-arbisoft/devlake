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

Both need a token with the `stats:read` scope, and both answer straight away with a `correlation_id`. A request is queued only if the client has an active `ClientConfigOutboundUrls` row for its event type: `engineer-stats` for the single API and `engineer-stats-batch` for the batch API. Without one, the request is logged as `Revoked - no callback URL` and gets a 422.

The same client sending the same emails and time frame within `Requests:DuplicateWindowSeconds` (300) gets the original `correlation_id`, and nothing new is queued. Revoked requests don't count as duplicates.

## Request queue

Stats requests go to the durable queue `RabbitMq:RequestQueue` as persistent messages. The processing job takes them in batches and acknowledges each one only after it is handled. A message that fails stays queued for the next run. If it fails again, it moves to `<RequestQueue>.failed`, where it stays for inspection or replay.

## Configuration

Secrets come from environment variables, never from `appsettings.json`.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__IntegrationDb` | MySQL connection string |
| `Security__EncryptionKey` | Base64 of a 32-byte AES key, for stored outbound credentials |
| `RabbitMq__Username`, `RabbitMq__Password` | RabbitMQ credentials, the same values as `RABBITMQ_USER` and `RABBITMQ_PASSWORD` in `.env` |
| `Secrets__<privateKeyRef>` | PEM private key of the signing key whose `privateKeyRef` is `<privateKeyRef>` |

## Test

```sh
dotnet test
```
