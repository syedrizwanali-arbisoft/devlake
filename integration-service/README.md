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

## Admin portal

`Arbisoft.DevLake.IntegrationService.Web` is the admin portal. One admin account is set through `Admin__Username` and `Admin__PasswordHash`, and every page needs that sign-in. The portal manages:

- **Clients:** create, edit, activate or deactivate, and set the token lifetime.
- **Secrets:** issue a new secret, shown once, then revoke the old one. Share it through Passbolt.
- **Scopes:** create them and assign them to clients.
- **Signing keys:** rotate. A new RSA key's private key is stored encrypted with `Security:EncryptionKey`, so the Web app and the API need the same encryption key. The previous key is retired, but the tokens it signed keep verifying until they expire.
- **Access tokens:** list them and revoke one with a reason.

Changes record the admin as `createdBy` or `modifiedBy`. A revoked token records the admin in its reason.

The portal also manages the integration side:

- **Email mappings:** list, search, create and edit the other emails each engineer commits with. Emails are stored lowercase, and Arbisoft emails must be `@arbisoft.com`.
- **Outbound config:** one per client. It sets the auth type (`oauth2` or `api_token`), base URL, max delivery attempts and active flag. Secrets are written once and stored encrypted; leaving one blank keeps the stored secret. The page shows when the credential was last rotated and when it expires.
- **Callback URLs:** the event type to method and path mappings for each config. Only one URL can be active per event type.
- **Request logs:** filter by client, status and UTC dates, and open a request to see its JSON body and every delivery attempt with the client's response.

## Stats APIs

| API | Rate limit per client |
|---|---|
| `GET /api/v1/engineers/{email}/stats?tf=24h` | `RateLimits:StatsPerMinute` (100) |
| `POST /api/v1/engineers/stats/batch` | `RateLimits:BatchStatsPerMinute` (20) |
| `GET /api/v1/requests/poll/{uuid}` | `RateLimits:PollPerMinute` (100) |

All three need a token with the `stats:read` scope. The stats APIs answer straight away with a `correlation_id`, and the poll API returns that request's current status: `Queued`, `Processing`, `Success`, `Failure` or `Revoked`. A client can only poll its own requests; for anyone else's, the poll API returns 404. A request is queued only if the client has an active `ClientConfigOutboundUrls` row for its event type: `engineer-stats` for the single API and `engineer-stats-batch` for the batch API. Without one, the request is logged as `Revoked - no callback URL` and gets a 422.

The same client sending the same emails and time frame within `Requests:DuplicateWindowSeconds` (300) gets the original `correlation_id`, and nothing new is queued. Revoked requests don't count as duplicates.

## Workstream projects API

`GET /fetch/projects` needs a token with the `projects:fetch` scope. It checks Workstream's token, reads every page of Workstream's `/projects`, and returns `{ count, projects: [{ id, name, team_lead: { name, email }, source }] }`. If the token check fails or any page fails, it returns 502 with an `error` and `workstream_token_rejected`, never a partial list.

DevLake calls this from its **Project Ingestion** screen (admins only), through `POST /projects/load`. To set it up:

1. In the admin portal, create a client for DevLake with the `projects:fetch` scope, and issue it a secret.
2. Set `INTEGRATION_SERVICE_URL`, `INTEGRATION_SERVICE_CLIENT_ID` and `INTEGRATION_SERVICE_CLIENT_SECRET` in DevLake's `.env`.

DevLake saves each project and its team lead in `workstream_projects`, keyed by Workstream ID, so loading again updates rather than duplicates. It links each project to the DevLake project with the same name and never creates DevLake projects.

Workstream's API isn't documented yet, so the mapping tries common field names. The ID comes from `id`, `project_id` or `projectId`, and the name from `name`, `title` or `project_name`. The team lead comes from an object (`team_lead`, `teamLead` or `lead`, with `name` or `full_name` and `email`) or from flat fields (`team_lead_name`, `team_lead_email`). `source` holds the original item, so nothing is lost.

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
| DevLake | `DevLake:BaseUrl` (ends in `/rest/`), `DevLake__ApiToken`, `DevLake:TimeoutSeconds` | `GET is_valid`, `GET stats/{email}/{jira,github,ai_usage,ai_cost}?tf=` |
| Workstream | `Workstream:BaseUrl`, `Workstream:PageSize`, `Workstream:TimeoutSeconds` | `GET is_valid`, `GET projects?page=&pageSize=` until the last page |
| Sophia | `ClientConfigs` and `ClientConfigOutboundUrls` rows; `Sophia:TimeoutSeconds` | OAuth2 token request, then the callback |

Every client has a total timeout and retries transient failures (5xx, 408, 429 and network errors) with backoff. Sophia's callback is sent once with no HTTP retry, because the processing job records each attempt and retries up to `maxAttempts`. A client config's `authType` is `oauth2` (client credentials) or `api_token` (a static bearer token), and its secrets are stored encrypted with `Security:EncryptionKey`.

### DevLake endpoints

DevLake serves these for the Integration Service (`backend/server/api/integration`). They are reached through `/rest/...`. There, DevLake checks `Authorization: Bearer <API key>` and the key's allowed path, so create a DevLake API key (Settings, then API keys) with the allowed path `^/(is_valid|stats/.+)$` and set it as `DevLake__ApiToken`. A direct call without `/rest` gets 401, even when DevLake's login is turned off.

- **`GET is_valid`:** `true`. An invalid, expired or out-of-scope key gets 403 from DevLake, which the client reports as `false`.
- **Every stats response** starts with `email` (lowercased), `timeFrame`, and `from` and `to` (RFC 3339 UTC, `[from, to)`). An email with no data gets zeros and empty lists. A bad email or `tf` gets 400, with a message naming the field.

| Category | Fields after the common ones |
|---|---|
| `jira` | `issuesResolved`, `storyPointsResolved`, `issuesCreated`, and the current `issuesInProgress` and `issuesTodo` |
| `github` | `commits`, `additions`, `deletions`, `pullRequestsOpened`, `pullRequestsMerged`, `pullRequestsWithCommits`, `reviews` |
| `ai_usage` | `providers: [{ provider, activeDays, sessions, suggestions, acceptances, linesAdded, linesRemoved, commitsByAi, pullRequests }]`, `toolEditsAccepted`, `toolEditsRejected` |
| `ai_cost` | `models: [{ provider, model, inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, estimatedCostUsd }]`, `totalEstimatedCostUsd` (decimal strings in USD) |

Some data limits apply:

- **Jira:** Jira stats and GitHub pull request authorship and reviews match on the account's email, which many Jira and GitHub accounts hide. Commit counts and `pullRequestsWithCommits` use the commit author email, which is reliable.
- **AI data:** it is daily, so AI windows cover whole UTC days. Workstream's API isn't documented yet. The client accepts each page either as a JSON array, which ends with a short page, or as `{ "results": [...], "next": ... }`, which ends when `next` is null.

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
| `Secrets__<privateKeyRef>` | Optional. A signing private key as PEM. Keys rotated in the admin portal are stored encrypted in `SecretStoreEntries` instead; the environment variable wins when both exist |
| `Admin__Username`, `Admin__PasswordHash` | The admin portal's sign-in. Create the hash with `echo '<password>' \| dotnet run --project Arbisoft.DevLake.IntegrationService.Web -- hash-password` |

## Test

```sh
dotnet test
```

The repository tests run the SQL against a real MySQL with the real migrations. They are skipped unless `INTEGRATION_TEST_MYSQL` holds a connection string for a user that can create databases. Each run creates its own `integration_test_*` database and drops it afterwards. To run them against the compose MySQL:

```sh
export INTEGRATION_TEST_MYSQL="Server=127.0.0.1;Port=3308;User ID=root;Password=<INTEGRATION_DB_ROOT_PASSWORD>"
dotnet test
```

CI starts a MySQL service for them and uploads a Cobertura coverage report (`--collect "Code Coverage;Format=cobertura"`).
