# Deploy WordGameBff to Fly.io + embed to Netlify

Multi-instance BFF using Fly Postgres for SignalR backplane and shared KV state.
Day-to-day deploys run via GitHub Actions after a one-time bootstrap.

## Prerequisites

- [Fly CLI](https://fly.io/docs/hands-on/install-flyctl/) installed and authenticated (`fly auth login`)
- CustomAuth M2M client id/secret
- wordgames Fly app URL (default in [`fly.toml`](../fly.toml): `https://wordgames-api.fly.dev`)
- Netlify site for the embed CDN (for CORS origins in `fly.toml`)

## Config split (CustomAuth pattern)

| Where | What |
|-------|------|
| [`fly.toml`](../fly.toml) `[env]` | Non-secrets: `GameApi__BaseUrl`, `CustomAuth__Authority` / `Audience`, `Session__Issuer` / `ExpiryMinutes`, `Cors__AllowedOrigins__*`, Realtime/Stores/PoW |
| Fly **secrets** | Credentials only: `REALTIME__BACKPLANE__CONNECTIONSTRING` (Npgsql), `SESSION__SIGNINGKEY`, `CUSTOMAUTH__CLIENTID` / `CLIENTSECRET` |
| GitHub Actions secrets | Deploy tokens only: `FLY_API_TOKEN`, `NETLIFY_AUTH_TOKEN`, `NETLIFY_SITE_ID` |

`fly postgres attach` sets `DATABASE_URL` (URI). **Do not use it** for the BFF — same as CustomOAuthServer. Set an explicit Npgsql string with `SSL Mode=Require`.

## 1. Netlify site (embed CDN)

Create a Netlify site pointed at `frontend/` (or create empty and let Actions deploy). Note the public URL and site ID.

Update CORS in [`fly.toml`](../fly.toml):

```toml
Cors__AllowedOrigins__0 = "https://wordgameui.netlify.app"
Cors__AllowedOrigins__1 = "https://nepalishabda.netlify.app"
Cors__AllowedOrigins__2 = "http://localhost:5173"
Cors__AllowedOrigins__3 = "http://localhost:3000"
```

## 2. Bootstrap Fly (app + Postgres + secrets)

```bash
cp .env.fly.example .env.fly
# Fill CUSTOMAUTH__CLIENTID / CLIENTSECRET and REALTIME__BACKPLANE__CONNECTIONSTRING
# (or FLY_PG_HOST / FLY_PG_DB / FLY_PG_USER / FLY_PG_PASSWORD)

./scripts/setup-fly.sh
```

The script creates `wordgamebff` + `wordgamebff-db`, attaches Postgres, and sets credential secrets only.

To build the Npgsql string yourself after create/attach:

```bash
fly postgres db list -a wordgamebff-db
# → Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require
```

Schema `bff` (`bff.store`, `bff.game_revisions`) is created automatically on first boot via `PostgresSchemaInitializer`.

## 3. GitHub Actions secrets

```bash
fly tokens create deploy -a wordgamebff -x 999999h
# → Settings → Secrets → Actions → FLY_API_TOKEN

# Also set:
#   NETLIFY_AUTH_TOKEN  (Netlify personal access token)
#   NETLIFY_SITE_ID     (Netlify site API ID)
```

## 4. Deploy

Push to `main` (path-filtered) or run **Deploy BFF** / **Deploy frontend** via `workflow_dispatch`.

Manual alternative:

```bash
fly deploy --remote-only
# fly.toml already has min_machines_running = 2 and [[vm]] memory = "512mb"
```

### Upstream wordgames-api must stay warm

The BFF proxies every game read and mutation to `wordgames-api`, and
`GameHubJoinService` verifies membership there on each SignalR connect. If that
app scales to zero, the first request after idle pays a JVM cold start — measured
at ~20s versus ~0.1s warm — which surfaces as slow joins and stalled realtime
updates.

Keep at least one upstream machine from stopping:

```bash
# Always-on machine (equivalent to min_machines_running = 1)
fly machine update <machine-id> -a wordgames-api --autostop=off --autostart

# Remaining machines: suspend resumes far faster than a cold stop for a JVM
fly machine update <machine-id> -a wordgames-api --autostop=suspend --autostart

fly machine list -a wordgames-api   # expect one started, checks 1/1
```

Prefer setting `min_machines_running = 1` and `auto_stop_machines = "suspend"` in
that app's own `fly.toml` so the setting survives its deploys. Co-locating it in
`iad` also removes a cross-region hop, since the BFF runs there.

## 5. Verify

```bash
curl https://wordgamebff.fly.dev/health/live
curl https://wordgamebff.fly.dev/health/ready

fly machine list -a wordgamebff   # expect shared-cpu-1x:512MB

# Upstream should answer warm, not cold-start
curl -s -o /dev/null -w 'total=%{time_total}s\n' \
  https://wordgames-api.fly.dev/q/health/live

curl -i -X OPTIONS 'https://wordgamebff.fly.dev/api/me' \
  -H 'Origin: https://wordgameui.netlify.app' \
  -H 'Access-Control-Request-Method: GET' \
  -H 'Access-Control-Request-Headers: authorization'
```

Expect `Access-Control-Allow-Origin` matching your Netlify or host origin.

## 6. Multi-instance checks

Before go-live:

1. Auth round-robin: `GET /auth/challenge` then `POST /auth/verify` — must succeed across instances
2. Logout: `POST /auth/logout` then verify token rejected on subsequent requests
3. Realtime: two browsers in the same game both receive `gameChanged` via SignalR
4. SignalR: WebSocket connects over `wss://wordgamebff.fly.dev/hubs/game?...`

## Load smoke

The scheduled `Load Smoke` workflow runs ten concurrent clients against the
hermetic stack. Run the same bounded check locally with:

```bash
./scripts/run-load-hermetic.sh
```

For a manual Fly check, create a waiting game and use a small VU count. Each VU
mints a PoW session and joins the game during setup; the default of three stays
below the production auth rate limit:

```bash
k6 run \
  -e BFF_URL=https://wordgamebff.fly.dev \
  -e GAME_ID=<waiting-game-id> \
  -e VUS=3 \
  load/fly-manual.js
```

For higher concurrency, supply comma-separated session tokens that are already
members of the game. This avoids spending the production PoW/auth budget:

```bash
k6 run \
  -e BFF_URL=https://wordgamebff.fly.dev \
  -e GAME_ID=<game-id> \
  -e SESSION_TOKENS='<token-1>,<token-2>,<token-3>' \
  load/fly-manual.js
```

Both scripts fail when request errors reach 5%, `getGame` p95 reaches 2s, or
SignalR handshake p95 reaches the 3s hub-join budget.

## Netlify embed CDN

See [frontend/HOST-INTEGRATION.md](../frontend/HOST-INTEGRATION.md). Deploy uses [`frontend/netlify.toml`](../frontend/netlify.toml) via the **Deploy frontend** workflow.

## Environment reference

### `fly.toml` `[env]` (not secret)

| Variable | Purpose |
|----------|---------|
| `GameApi__BaseUrl` | wordgames Fly app URL |
| `CustomAuth__Authority` / `Audience` | Public OIDC settings |
| `Session__Issuer` / `ExpiryMinutes` | Session JWT metadata |
| `Cors__AllowedOrigins__N` | Netlify CDN + host app origins |
| `Realtime__*` / `Stores__Type` / `POW__*` | Production realtime + PoW |

### Fly secrets (credentials)

| Variable | Purpose |
|----------|---------|
| `SESSION__SIGNINGKEY` | BFF session JWT HMAC key (>= 32 chars) |
| `CUSTOMAUTH__CLIENTID` / `CLIENTSECRET` | M2M token exchange |
| `REALTIME__BACKPLANE__CONNECTIONSTRING` | Npgsql Postgres (backplane + KV stores); ignore attach `DATABASE_URL` |
