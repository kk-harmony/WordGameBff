# Host integration guide

Checklist for embedding the WordGame micro frontend in a host application.

## 0. Mobile-ready host page

Include a viewport meta tag so the widget scales correctly on phones and tablets:

```html
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
```

The embed widget caps itself at 480px width and includes touch-friendly controls, but the host page must provide viewport configuration and adequate horizontal padding.

## 1. Register CORS origin on WordGameBff BFF

The host origin must be listed in BFF configuration.

**Development** (`appsettings.Development.json`):

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:3000",
    "http://localhost:5173"
  ]
}
```

**Production — Fly.io BFF + Netlify CDN** (environment variables):

```bash
# Fly.io secrets (BFF)
Cors__AllowedOrigins__0=https://your-embed.netlify.app
Cors__AllowedOrigins__1=https://your-host-app.com
```

Also register the Netlify site URL if the embed is loaded directly from Netlify (not just the host app).

Verify preflight against your Fly BFF:

```bash
curl -i -X OPTIONS 'https://wordgamebff.fly.dev/api/me' \
  -H 'Origin: https://your-embed.netlify.app' \
  -H 'Access-Control-Request-Method: GET' \
  -H 'Access-Control-Request-Headers: authorization'
```

Expect `Access-Control-Allow-Origin: https://your-embed.netlify.app`.

## 2. Load embed script (Netlify CDN)

Production embed assets are deployed to Netlify from `frontend/` (see `netlify.toml`). Assets are served at versioned paths:

```
https://your-embed.netlify.app/v1.0.0/embed.js
https://your-embed.netlify.app/v1.0.0/sri.txt
```

For local Docker CDN instead:

```
https://cdn.example.com/v1.0.0/embed.js
https://cdn.example.com/v1.0.0/sri.txt
```

Generate SRI locally:

```bash
cd frontend && npm run build && npm run sri
cat dist/embed/v1.0.0/sri.txt
```

Example script tag:

```html
<script
  src="https://cdn.example.com/v1.0.0/embed.js"
  integrity="sha384-REPLACE_WITH_sri.txt"
  crossorigin="anonymous"
></script>
```

Replace `sha384-REPLACE_WITH_sri.txt` with the contents of `sri.txt` from the same build.

## 3. Add the Web Component

```html
<word-game-widget
  api-base="https://wordgamebff.fly.dev"
  locale="en"
  theme="light"
></word-game-widget>
```

Optional attributes:

| Attribute | Description |
|-----------|-------------|
| `api-base` | WordGameBff BFF URL (HTTPS required except localhost and private LAN IPs in development) |
| `game-id` | Auto-join this game on load |
| `locale` | Locale code (`en` only in v1) |
| `theme` | `light` or `dark` |
| `debug` | Log event types/IDs to console (never tokens) |

`session-token` is observed but **not used in v1** (PoW auth only).

## 4. Listen for events

```javascript
const widget = document.querySelector('word-game-widget');

widget.addEventListener('wordgame:ready', () => {
  console.log('Widget ready');
});

widget.addEventListener('wordgame:session', (e) => {
  // { userId, expiresAt } — NO sessionToken
  console.log('User:', e.detail.userId);
});

widget.addEventListener('wordgame:game', (e) => {
  console.log('Game update:', e.detail.gameId, e.detail.status);
});

widget.addEventListener('wordgame:disconnected', () => {});
widget.addEventListener('wordgame:reconnected', () => {});
widget.addEventListener('wordgame:error', (e) => {
  console.error(e.detail.message);
});
```

Programmatic mount (optional):

```javascript
const instance = window.WordGame.mount({
  apiBase: 'https://wordgamebff.fly.dev',
  container: document.getElementById('game-root'),
  theme: 'dark',
});
// instance.dispose() when done
```

## 5. Sticky sessions (SignalR)

Place a load balancer with **sticky sessions** in front of multiple BFF instances. WebSocket connections must stay on the same instance; REST can round-robin.

Hub URL (internal to widget): `{api-base}/hubs/game?gameId={id}&access_token={token}`

## 6. Content Security Policy

Recommended directives:

```
script-src 'self' https://your-embed.netlify.app;
connect-src 'self' https://wordgamebff.fly.dev wss://wordgamebff.fly.dev;
style-src 'self' 'unsafe-inline';
```

The widget uses Shadow DOM with inline styles. Allow `'unsafe-inline'` for styles inside the shadow tree, or use a nonce strategy if your CSP supports shadow DOM nonces.

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| CORS error on `/auth/challenge` | Host origin not registered | Add origin to `Cors:AllowedOrigins` |
| WebSocket fails silently | Corporate proxy / missing `wss://` in CSP | Allow `wss://bff.example.com` in `connect-src` |
| `429 Too Many Requests` | Rate limit exceeded | Widget shows retry guidance; wait for `Retry-After` |
| Widget stuck on Authenticating | PoW difficulty too high / BFF down | Check BFF health; dev uses low difficulty |
| `api-base must use HTTPS` | Non-local HTTP in production | Use `https://` BFF URL |

## 8. Health checks

Netlify serves static embed assets only (no health endpoint).

BFF on Fly.io:

```bash
curl https://wordgamebff.fly.dev/health/live    # {"status":"healthy"}
curl https://wordgamebff.fly.dev/health/ready   # {"status":"healthy"} when Postgres is reachable
curl http://localhost:8080/health               # legacy liveness alias (local dev)
```

Local Docker CDN:

```bash
curl http://localhost:8082/health   # ok
```
