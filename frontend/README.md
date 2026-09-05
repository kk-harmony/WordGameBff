# WordGame Micro Frontend

Production-ready, framework-agnostic embeddable game UI for WordGameBff.

## Packages

| Package | Purpose |
|---------|---------|
| `@wordgame/sdk` | API client, PoW auth, SignalR — no DOM |
| `@wordgame/ui` | Game views and state rendering |
| `@wordgame/embed` | Web Component + `embed.js` IIFE bundle |
| `@wordgame/playground` | Local dev host (not shipped) |
| `@wordgame/e2e` | Playwright tests |

## Prerequisites

- Node.js 22+
- WordGameBff BFF running at `http://localhost:8180` (see repo root README)

## Local development

```bash
cd frontend
cp .env.example .env
npm ci
npm run dev
```

Open [http://localhost:5174](http://localhost:5174). The playground loads `embed.js` via script tag from `/embed/v1.0.0/embed.js` (built on startup).

To use the CDN Docker service instead:

```bash
# from repo root
docker compose up --build wordgamebff embed
```

```bash
VITE_EMBED_CDN=http://localhost:8083 npm run dev
# or: http://localhost:5174/?embedCdn=http://localhost:8083
```

## Scripts

| Command | Description |
|---------|-------------|
| `npm run dev` | Build embed + start playground on `:5174` |
| `npm run build` | Build sdk, ui, embed |
| `npm run test` | SDK unit tests (Vitest) |
| `npm run test:e2e` | Playwright E2E (requires stack + playground) |
| `npm run lint` | ESLint + TypeScript (`tsc --noEmit`) |
| `npm run sri` | Generate SHA-384 SRI hash → `dist/embed/v{version}/sri.txt` |
| `npm run check:bundle-size` | Fail if gzipped `embed.js` > 150 KB |

## Docker (CDN)

```bash
docker build -f frontend/Dockerfile frontend/
docker run --rm -p 8083:80 wordgame-embed
curl http://localhost:8083/health        # ok
curl -I http://localhost:8083/v1.0.0/embed.js  # immutable cache
```

Or via compose (port `8083`):

```bash
docker compose up --build embed wordgamebff
```

## CI

GitHub Actions workflow: `.github/workflows/frontend-ci.yml`

- Push/PR affecting `frontend/**`: lint → test → build → bundle size → SRI
- Main branch: E2E job with docker-compose stack

## Release process

1. Bump `@wordgame/embed` version in `packages/embed/package.json`
2. `npm ci && npm run build && npm run check:bundle-size && npm run sri`
3. Build and push Docker image
4. Deploy Netlify CDN (see `netlify.toml`)
5. Update host apps to the new versioned URL + SRI hash
