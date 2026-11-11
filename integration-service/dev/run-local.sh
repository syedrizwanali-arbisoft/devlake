#!/usr/bin/env bash
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
# Starts or stops the Integration Service API, admin portal and the Workstream/Sophia stubs on this machine.
# Settings and generated secrets come from integration-service/.env (gitignored). Logs go to dev/logs/.
#   dev/run-local.sh start | stop | status
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$(pwd); LOGS="$ROOT/dev/logs"; mkdir -p "$LOGS"
set -a; . ./.env; set +a
DOTNET="${DOTNET_ROOT:+$DOTNET_ROOT/}dotnet"
P=Arbisoft.DevLake.IntegrationService

start_one() { # name, pid file, command...
  local name=$1 pidfile="$LOGS/$1.pid"; shift
  if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then echo "$name already running"; return; fi
  nohup "$@" > "$LOGS/$name.log" 2>&1 & echo $! > "$pidfile"; echo "started $name (log: dev/logs/$name.log)"
}

case "${1:-}" in
  start)
    docker compose up -d --wait mysql rabbitmq >/dev/null
    "$DOTNET" build "$P.API" -v q >/dev/null && "$DOTNET" build "$P.Web" -v q >/dev/null
    export ConnectionStrings__IntegrationDb="Server=127.0.0.1;Port=3308;Database=integration;User ID=integration;Password=$INTEGRATION_DB_PASSWORD"
    export Security__EncryptionKey="$SECURITY_ENCRYPTION_KEY" RabbitMq__Username="$RABBITMQ_USER" RabbitMq__Password="$RABBITMQ_PASSWORD"
    start_one stubs python3 -I dev/stubs.py
    (cd "$P.API" && ASPNETCORE_URLS=http://127.0.0.1:5099 DevLake__BaseUrl=http://localhost:8080/rest/ DevLake__ApiToken="${DEVLAKE_API_TOKEN:-}" \
      Workstream__BaseUrl=http://127.0.0.1:5097/ws/ Workstream__ApiToken="$WORKSTREAM_TOKEN" Processing__IntervalSeconds=15 Processing__DeliveryRetryDelaySeconds=2 \
      start_one api "$DOTNET" "bin/Debug/net10.0/$P.API.dll")
    (cd "$P.Web" && ASPNETCORE_URLS=http://127.0.0.1:5098 Admin__Username="$ADMIN_USERNAME" Admin__PasswordHash="$ADMIN_PASSWORD_HASH" \
      start_one web "$DOTNET" "bin/Debug/net10.0/$P.Web.dll")
    ;;
  stop)
    for name in web api stubs; do
      pidfile="$LOGS/$name.pid"
      if [ -f "$pidfile" ] && kill "$(cat "$pidfile")" 2>/dev/null; then echo "stopped $name"; fi
      rm -f "$pidfile"
    done ;;
  status)
    for name in api web stubs; do
      pidfile="$LOGS/$name.pid"
      if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then echo "$name running"; else echo "$name stopped"; fi
    done ;;
  *) echo "usage: dev/run-local.sh start|stop|status"; exit 1 ;;
esac
