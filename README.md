# WordGameBff

ASP.NET Core 10 BFF (Backend for Frontend) that sits in front of the [wordgames](https://github.com) Quarkus API. Browser clients authenticate via Proof of Work, receive BFF session JWTs, and interact with games only through this service. Upstream wordgames calls use CustomAuth M2M token exchange (server-side only).

## Architecture

```
Browser  →  WordGameBff (PoW + session JWT + SignalR)
                 ↓ CustomAuth token exchange
            wordgames (internal Docker network)
                 ↓
            PostgreSQL (wordgame DB)

WordGameBff instances share realtime events via PostgreSQL NOTIFY/LISTEN backplane.
```

### Layers

| Project | Responsibility |
|---------|----------------|
| `WordGameBff.Api` | HTTP endpoints, SignalR hub wiring, middleware |
| `WordGameBff.Application` | PoW, session tokens, game event publishing, abstractions |
| `WordGameBff.Domain` | Shared models |
| `WordGameBff.Infrastructure` | CustomAuth, wordgames HTTP client, SignalR, Postgres backplane |

## Quick start

### Local development

```bash
cp .env.example .env
# Edit .env with CustomAuth M2M credentials

dotnet run --project src/WordGameBff.Api
curl http://localhost:8080/health
```

Development uses low PoW difficulty (`appsettings.Development.json`) and an in-memory realtime backplane.

### Test on other devices (same WiFi)

Use this to exercise the micro frontend on phones and tablets while developing locally.

1. Allow incoming connections on ports **8080** (BFF) and **5173** (playground) in your OS firewall.
2. Find your machine's LAN IP (macOS: `ipconfig getifaddr en0`).
3. Start the BFF (binds all interfaces in Development):

   ```bash
   dotnet run --project src/WordGameBff.Api
   ```

4. Start the frontend playground (also binds all interfaces):

   ```bash
   cd frontend && npm run dev
   ```

5. On another device on the same network, open `http://<LAN-IP>:5173`.

   The playground resolves `api-base` to `http://<LAN-IP>:8080` automatically when not opened via localhost. Override with `?apiBase=http://<LAN-IP>:8080` if needed.

6. Verify the BFF from the device network:

   ```bash
   curl http://<LAN-IP>:8080/health
   ```

For the Podman demo (`http://<LAN-IP>:3000`), the demo page uses the same dynamic `api-base` resolution.

In Development, the BFF also accepts CORS preflights from `http://` origins on loopback and private LAN IPs (e.g. `http://192.168.x.x:5173`).

### Docker Compose

Set `WORDGAMES_BUILD_CONTEXT` to your local wordgames repo path (default in compose file points to `../../learningProjects/wordgames`).

```bash
cp .env.example .env
# Set CUSTOMAUTH__CLIENTID and CUSTOMAUTH__CLIENTSECRET

docker compose up --build
curl http://localhost:8080/health   # wordgamebff — OK
curl http://localhost:8081          # wordgames — should fail (not exposed)
```

Multi-instance testing:

```bash
docker compose --profile multi-instance up --build
```

Realtime clients connect with **WebSockets + skipNegotiation**, so SignalR does not need sticky sessions for the negotiate handshake. REST can round-robin; each WebSocket stays on one instance and realtime delivery fans out via the Postgres backplane.


### Podman + host PostgreSQL (recommended local full stack)

Uses **locally installed PostgreSQL** and **Podman** for wordgames + wordgamebff + an HTML demo page with the micro frontend. **CustomAuth** uses production `https://customauth.fly.dev/` only (no mocks).

**Prerequisites:** Podman, PostgreSQL (running on `localhost:5432`), [wordgames](https://github.com) repo checkout, CustomAuth M2M credentials.

```bash
cp .env.example .env
# Set CUSTOMAUTH__CLIENTID, CUSTOMAUTH__CLIENTSECRET, POSTGRES_USER, POSTGRES_PASSWORD

./scripts/run-podman-local.sh
# Choose: 4) Init DB (first time), then 1) Up
```

Open the demo: **http://localhost:3000** — plain HTML embedding `<word-game-widget>`.

Verify:

```bash
curl http://localhost:8080/health   # wordgamebff — OK
curl http://localhost:3000/health   # demo nginx — OK
curl http://localhost:8081          # wordgames — should fail (not exposed)
```

Containers connect to host Postgres via `host.containers.internal`. Databases `wordgame` and `wordgamebff` are created on the host (not in Podman).

Non-interactive: `./scripts/run-podman-local.sh up`


## Configuration

| Key | Purpose |
|-----|---------|
| `GameApi:BaseUrl` | wordgames base URL (internal) |
| `CustomAuth:Authority` | OIDC issuer |
| `CustomAuth:ClientId` / `ClientSecret` | M2M client |
| `CustomAuth:Audience` | Upstream audience (`wordgame`) |
| `Session:SigningKey` | BFF session JWT HMAC key |
| `Session:ExpiryMinutes` | Session TTL |
| `Pow:DifficultyBits` | PoW leading zero bits |
| `Pow:ChallengeExpirySeconds` | Challenge TTL |
| `Cors:AllowedOrigins` | Allowed browser origins |
| `Realtime:Transport` | `SignalR` (default) |
| `Realtime:BackplaneType` | `PostgreSQL` or `InMemory` |
| `Realtime:Backplane:ConnectionString` | Postgres for NOTIFY/LISTEN |

Environment variable form: `Section__Key` (e.g. `SESSION__SIGNINGKEY`).

## CustomAuth integration

Discovery document: `https://customauth.fly.dev/.well-known/openid-configuration`

| Finding | Value |
|---------|-------|
| Token endpoint | `https://customauth.fly.dev/connect/token` |
| BFF grant | **`client_credentials`** (`scope=api`) only |

The BFF authenticates to CustomAuth as a **service client** (`wordgamebff`). PoW players are anonymous synthetic users identified by the BFF session JWT — they are **not** CustomAuth users.

Upstream calls to wordgames use:

1. `Authorization: Bearer {service_token}` — from `client_credentials`
2. `X-Delegated-User-Id: {sub}` — PoW user id from the BFF session

wordgames trusts delegation only from the configured BFF client id (`BFF_CLIENT_ID` / `app.bff.client-id`).

The service token is cached until 30 seconds before expiry.

### Manual verification

```bash
# Requires valid M2M credentials in .env
dotnet test --filter "CustomAuthTokenServiceTests"
```

## Authentication flow

1. `GET /auth/challenge` — receive `{ challengeId, prefix, difficulty, expiresAt }`
2. Client finds `nonce` where `SHA-256(prefix + nonce)` has `difficulty` leading zero bits
3. `POST /auth/verify` — `{ challengeId, nonce }` → `{ sessionToken, userId, expiresAt }`
4. Send `Authorization: Bearer {sessionToken}` on `/api/*`
5. Optional: `POST /auth/logout` revokes session id (`sid` claim)

## REST API (proxied)

| WordGameBff | Upstream |
|---------|----------|
| `GET /api/me` | Returns BFF session `{ userId }` (not proxied) |
| `POST /api/games` | `POST /games` (BFF returns **201** + `Location`) |
| `GET /api/games/{id}` | `GET /games/{id}` |
| `POST /api/games/{id}/rounds` | `POST /games/{id}/start` (begin play / first round) |
| `POST /api/games/{id}/members` | join |
| `DELETE /api/games/{id}/members/{userId}` | leave / kick |
| `POST /api/games/{id}/turns` | `POST /games/{id}/turn/complete` |
| `GET /api/games/{id}/assigned-word` | `GET /games/{id}/my-word` |
| `GET /api/games/{id}/word-pair` | Finished games only: authentic + imposed from upstream finished `secretWord` |
| `POST /api/games/{id}/votes` | `POST /games/{id}/vote` |
| `POST /api/games/{id}/votes` | `POST /games/{id}/vote` |
| `GET /api/games/{gameId}/secret-words/random` | `GET /secretwords/random` (game-scoped access) |
| `GET /api/games/{gameId}/secret-words/{id}` | `GET /secretwords/{id}` (game-scoped access) |
| `POST /api/secret-words` | `POST /secretwords` (global catalog; intentional vs nested reads) |

Errors: `{ "error": "CODE", "message": "..." }`

## Realtime wire protocol

SignalR carries **lightweight change notifications** only. Clients load authoritative state via `GET /api/games/{id}`.

```json
{
  "type": "gameChanged",
  "gameId": 1,
  "revision": 3,
  "triggeredBy": "user-guid",
  "action": "vote"
}
```

| Field | Meaning |
|--------|---------|
| `type` | Always `gameChanged` |
| `gameId` | Which game changed |
| `revision` | Monotonic counter for ordering/dedup |
| `triggeredBy` | User who caused the mutation |
| `action` | What happened (see below) |

| `action` | Typical client follow-up |
|----------|----------------------------|
| `join` | `GET /api/games/{id}` |
| `leave` | `GET /api/games/{id}` |
| `start` | `GET /api/games/{id}` + `GET /api/games/{id}/assigned-word` |
| `turnComplete` | `GET /api/games/{id}` (+ `assigned-word` if still playing) |
| `vote` | `GET /api/games/{id}` (+ `word-pair` when finished) |
| `memberRemoved` | `GET /api/games/{id}` |

Unknown `action` values should fall back to a full game refresh.

Sensitive fields are stripped on `GET /api/games/{id}`: `impostorUserId` (until finished), member `assignedWord`, and nested `secretWord`. After finish, members load both words via `GET /api/games/{id}/word-pair`.

### SignalR adapter

- Hub URL: `/hubs/game?gameId={id}&access_token={sessionToken}`
- Server method: `gameEvent` — payload is `GameRealtimeMessage` JSON
- Hub connect validates membership; no game snapshot is pushed
- Clients reconcile via `GET /api/games/{id}` on each notification (and poll while disconnected)

### Backplane

PostgreSQL `NOTIFY wordgamebff_backplane` fan-out to all WordGameBff instances. Each instance delivers locally via `IGameRealtimeTransport` (SignalR groups).

## Rate limiting

| Policy | Scope | Limit |
|--------|-------|-------|
| `auth-ip` | `/auth/*` | 10/min per IP |
| `api-ip` | `/api/*` | 60/min per IP |
| `api-session` | `/api/*` | 120/min per `sub` |
| `hub-ip` | `/hubs/*` | 10 connect attempts/min per IP |

Returns `429` with `Retry-After` header.

## Security

- wordgames is on the Docker internal network only (no host port `8081`)
- Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`
- CORS uses explicit origins in production
- Authorization headers and tokens are not logged

## Production deployment

- **BFF:** Fly.io — see [docs/DEPLOY-FLY.md](docs/DEPLOY-FLY.md) (bootstrap once with `./scripts/setup-fly.sh`, then GitHub Actions **Deploy BFF** on `main`)
- **Embed CDN:** Netlify — [frontend/HOST-INTEGRATION.md](frontend/HOST-INTEGRATION.md), `frontend/netlify.toml`, and Actions **Deploy frontend**
- **wordgames:** independent Fly app; `GameApi__BaseUrl` is set in [`fly.toml`](fly.toml) `[env]`

Production uses Postgres for SignalR backplane and shared BFF state (schema `bff`: `bff.store`, `bff.game_revisions`). Multi-instance requires `Stores__Type=PostgreSQL` and `Realtime__BackplaneType=PostgreSQL`.

## Tests

```bash
dotnet build
dotnet test
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`net10.0`)
- Docker (optional, for Compose stack)
