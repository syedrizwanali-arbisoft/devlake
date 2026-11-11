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

"""Sets up a local Integration Service through the admin portal, like an admin would. Safe to rerun: it skips
what already exists. Prints only names; new secrets are appended to the .env files given."""
import html, http.cookiejar, os, re, sys, urllib.parse, urllib.request

PORTAL = "http://127.0.0.1:5098"
service_env, devlake_env = sys.argv[1], sys.argv[2]
jar = http.cookiejar.CookieJar()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect)


def call(path, data=None):
    req = urllib.request.Request(PORTAL + path, data=urllib.parse.urlencode(data, doseq=True).encode() if data is not None else None)
    try:
        with opener.open(req) as resp:
            return resp.status, resp.read().decode(), resp.headers.get("Location")
    except urllib.error.HTTPError as err:
        return err.code, err.read().decode(), err.headers.get("Location")


def post(form_page, path, fields):
    _, page, _ = call(form_page)
    token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)
    status, body, location = call(path, fields + [("__RequestVerificationToken", token)])
    if status not in (200, 302):
        raise SystemExit(f"{path} returned {status}")
    errors = re.findall(r'<div class="errors[^"]*"[^>]*>\s*<ul>(.*?)</ul>', body, re.S)
    return location, html.unescape(re.sub("<[^>]+>", " ", errors[0])).strip() if errors else None


def env_has(path, key):
    return os.path.exists(path) and any(line.startswith(key + "=") for line in open(path))


def env_add(path, key, value):
    if not env_has(path, key):
        with open(path, "a") as f:
            f.write(f"{key}={value}\n")


env = dict(line.strip().split("=", 1) for line in open(service_env) if "=" in line and not line.startswith("#"))
post("/Account/Login", "/Account/Login", [("Username", env["ADMIN_USERNAME"]), ("Password", env["ADMIN_PASSWORD"])])
assert call("/Clients")[0] == 200, "sign-in failed"
print("signed in")

for name, description in [("stats:read", "Read engineer stats and poll requests"), ("projects:fetch", "Load Workstream projects")]:
    _, error = post("/Scopes/Create", "/Scopes/Create", [("Name", name), ("Description", description)])
    print(f"scope {name}: {'exists' if error else 'created'}")

if '<span class="badge Active">' not in call("/SigningKeys")[1]:
    post("/SigningKeys/Rotate", "/SigningKeys/Rotate", [])
    print("signing key: created")
else:
    print("signing key: exists")

scope_ids = dict((n, i) for i, n in re.findall(r'name="ScopeIds" value="(\d+)"[^>]*>\s*<span class="mono">([^<]+)</span>', call("/Clients/Create")[1] or ""))


def client(client_id, name, scope, env_path, id_key, secret_key):
    location, error = post("/Clients/Create", "/Clients/Create", [
        ("Form.ClientId", client_id), ("Form.Name", name), ("Form.AccessTokenLifetimeSeconds", "3600"),
        ("Form.IsActive", "true"), ("Form.IsActive", "false")])
    listing = call("/Clients")[1]
    ref = re.search(r'href="/Clients/Details/(\d+)">[^<]*</a></td>\s*<td class="mono">' + re.escape(client_id) + "<", listing).group(1)
    print(f"client {client_id}: {'exists' if error else 'created'} (id {ref})")
    details = call(f"/Clients/Details/{ref}")[1]
    sid = re.search(r'name="ScopeIds" value="(\d+)"[^>]*>\s*<span class="mono">' + re.escape(scope) + "<", details).group(1)
    post(f"/Clients/Details/{ref}", f"/Clients/Details/{ref}?handler=Scopes", [("ScopeIds", sid)])
    if not env_has(env_path, secret_key):
        _, page, _ = call(f"/Clients/{ref}/Secrets/Create")
        token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)
        _, page, _ = call(f"/Clients/{ref}/Secrets/Create", [("ValidForDays", "365"), ("__RequestVerificationToken", token)])
        secret = html.unescape(re.search(r'<dt>Client secret</dt><dd class="mono long">([^<]+)</dd>', page).group(1))
        env_add(env_path, id_key, client_id)
        env_add(env_path, secret_key, secret)
        print(f"  secret issued, saved as {secret_key}")
    return ref


sophia = client("sophia-local", "Sophia (local stub)", "stats:read", service_env, "SOPHIA_CLIENT_ID", "SOPHIA_CLIENT_SECRET")
client("devlake-local", "DevLake (local)", "projects:fetch", devlake_env, "INTEGRATION_SERVICE_CLIENT_ID", "INTEGRATION_SERVICE_CLIENT_SECRET")
env_add(devlake_env, "INTEGRATION_SERVICE_URL", "http://host.docker.internal:5099/")

has_config = "Callback URLs" in call(f"/Clients/Details/{sophia}")[1]
fields = [("Form.AuthType", "oauth2"), ("Form.BaseUrl", "http://127.0.0.1:5097/sophia/"), ("Form.TokenUrl", "oauth2/token/"),
          ("Form.OutboundClientId", env["SOPHIA_OUTBOUND_CLIENT_ID"]), ("Form.MaxAttempts", "3"), ("Form.IsActive", "true"), ("Form.IsActive", "false")]
if not has_config:
    fields.append(("Form.OutboundClientSecret", env["SOPHIA_OUTBOUND_CLIENT_SECRET"]))
_, error = post(f"/ClientConfigs/Edit/{sophia}", f"/ClientConfigs/Edit/{sophia}", fields)
print(f"sophia outbound config: {'updated' if has_config else 'created'}{' (' + error + ')' if error else ''}")
_, error = post(f"/ClientConfigs/{sophia}/OutboundUrls/Create", f"/ClientConfigs/{sophia}/OutboundUrls/Create", [
    ("Form.EventType", "engineer-stats"), ("Form.Method", "POST"), ("Form.Path", "/api/v1/devlake/callback/"),
    ("Form.IsActive", "true"), ("Form.IsActive", "false")])
print(f"callback engineer-stats: {'exists' if error else 'created'}")
