# Deploy WordGameBff to Fly.io

Multi-instance BFF using Fly Postgres for SignalR backplane and shared KV state.

## Prerequisites

- [Fly CLI](https://fly.io/docs/hands-on/install-flyctl/) installed and authenticated
- CustomAuth M2M credentials
- wordgames app deployed separately (provide its URL)
- Netlify site URL for embed CDN (for CORS)

## 1. Create the Fly app

```bash
fly apps create wordgamebff
```

Or rename `app` in [`fly.toml`](../fly.toml) to match your app name.

## 2. Provision Postgres

```bash
fly postgres create --name wordgamebff-db --region iad
fly postgres attach wordgamebff-db --app wordgamebff
```

This sets `DATABASE_URL`. Map it to the BFF backplane connection string:

```bash
# Convert DATABASE_URL to Npgsql format if needed, then:
fly secrets set \
  REALTIME__BACKPLANE__CONNECTIONSTRING="Host=...;Port=5432;Database=wordgamebff;Username=...;Password=...;SSL Mode=Require"
```

Tables live under the dedicated Postgres schema `bff` (`bff.store`, `bff.game_revisions`). Created automatically on first startup via `PostgresSchemaInitializer`. You can also apply manually:

```bash
fly postgres connect -a wordgamebff-db < docker/bff-store-schema.sql
```

## 3. Set secrets

```bash
fly secrets set \
  SESSION__SIGNINGKEY="$(openssl rand -base64 48)" \
  CUSTOMAUTH__CLIENTID="your-client-id" \
  CUSTOMAUTH__CLIENTSECRET="your-client-secret" \
  GAMEAPI__BASEURL="https://wordgames.fly.dev" \
  CORS__ALLOWEDORIGINS__0="https://your-embed.netlify.app" \
  CORS__ALLOWEDORIGINS__1="https://your-host-app.com"
```

`Stores__Type=PostgreSQL` is set in `fly.toml` and reuses `REALTIME__BACKPLANE__CONNECTIONSTRING`.

## 4. Deploy

```bash
fly deploy
fly scale count 2
```

## 5. Verify

```bash
curl https://wordgamebff.fly.dev/health/live
curl https://wordgamebff.fly.dev/health/ready

curl -i -X OPTIONS 'https://wordgamebff.fly.dev/api/me' \
  -H 'Origin: https://your-embed.netlify.app' \
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

## Netlify embed CDN

See [frontend/HOST-INTEGRATION.md](../frontend/HOST-INTEGRATION.md). Deploy embed from `frontend/` with Netlify using `netlify.toml`.

## Environment reference

| Variable | Purpose |
|----------|---------|
| `SESSION__SIGNINGKEY` | BFF session JWT HMAC key (>= 32 chars) |
| `CUSTOMAUTH__CLIENTID` / `CLIENTSECRET` | M2M token exchange |
| `GAMEAPI__BASEURL` | wordgames Fly app URL |
| `REALTIME__BACKPLANE__CONNECTIONSTRING` | Fly Postgres (backplane + KV stores) |
| `STORES__TYPE` | `PostgreSQL` in production |
| `CORS__ALLOWEDORIGINS__N` | Netlify CDN + host app origins |
