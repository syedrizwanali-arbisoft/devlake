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
