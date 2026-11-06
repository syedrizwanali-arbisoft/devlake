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

## Configuration

Secrets come from environment variables, never from `appsettings.json`.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__IntegrationDb` | MySQL connection string |
| `Security__EncryptionKey` | Base64 of a 32-byte AES key, for stored outbound credentials |
| `Secrets__<privateKeyRef>` | PEM private key of the signing key whose `privateKeyRef` is `<privateKeyRef>` |

## Test

```sh
dotnet test
```
