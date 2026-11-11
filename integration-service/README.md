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

Health endpoints: `/health/live` and `/health/ready`.

## Build and test

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
```

CI runs the same steps plus a vulnerable-package check (`.github/workflows/integration-service.yml`).
