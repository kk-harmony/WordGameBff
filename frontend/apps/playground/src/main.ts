import { resolveDevApiBase } from '@wordgame/sdk';

const logEl = document.getElementById('event-log');
const hostEl = document.getElementById('widget-host');
let lastGameLogKey: string | null = null;

function logEvent(name: string, detail: unknown): void {
  if (!logEl) {
    return;
  }
  const safe = sanitizeDetail(detail);
  if (name === 'wordgame:game' && safe && typeof safe === 'object') {
    const record = safe as Record<string, unknown>;
    const key = `${String(record.gameId ?? '')}:${String(record.status ?? '')}`;
    if (key === lastGameLogKey) {
      return;
    }
    lastGameLogKey = key;
  }
  const entry = document.createElement('div');
  entry.className = 'log-entry';
  entry.textContent = `${new Date().toISOString()} ${name}: ${JSON.stringify(safe)}`;
  logEl.appendChild(entry);
  logEl.scrollTop = logEl.scrollHeight;
}

function sanitizeDetail(detail: unknown): unknown {
  if (detail && typeof detail === 'object') {
    const copy = { ...(detail as Record<string, unknown>) };
    delete copy['sessionToken'];
    return copy;
  }
  return detail;
}

function loadEmbedScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = src;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error(`Failed to load ${src}`));
    document.head.appendChild(script);
  });
}

function resolveEmbedScriptUrl(): string {
  const params = new URLSearchParams(window.location.search);
  const embedCdn = params.get('embedCdn') ?? __EMBED_CDN__;
  const version = __EMBED_VERSION__;
  if (embedCdn) {
    return `${embedCdn.replace(/\/$/, '')}/v${version}/embed.js`;
  }
  return `/embed/v${version}/embed.js`;
}

async function init(): Promise<void> {
  const params = new URLSearchParams(window.location.search);
  const apiBase = resolveDevApiBase(__API_BASE__);
  const embedSrc = resolveEmbedScriptUrl();

  await loadEmbedScript(embedSrc);

  if (!window.WordGame) {
    throw new Error('WordGame global not found after loading embed.js');
  }

  if (!hostEl) {
    return;
  }

  const widget = document.createElement('word-game-widget');
  widget.setAttribute('api-base', apiBase);
  widget.setAttribute('locale', 'en');
  widget.setAttribute('theme', 'light');
  if (params.get('debug') === '1') {
    widget.setAttribute('debug', '');
  }
  const gameId = params.get('gameId');
  if (gameId) {
    widget.setAttribute('game-id', gameId);
  }

  const events = [
    'wordgame:ready',
    'wordgame:session',
    'wordgame:game',
    'wordgame:disconnected',
    'wordgame:reconnected',
    'wordgame:error',
  ] as const;

  for (const name of events) {
    widget.addEventListener(name, (e) => {
      logEvent(name, (e as CustomEvent).detail);
    });
  }

  hostEl.appendChild(widget);
}

void init().catch((err) => {
  if (logEl) {
    logEl.textContent = err instanceof Error ? err.message : String(err);
  }
});
