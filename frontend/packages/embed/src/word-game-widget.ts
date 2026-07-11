import { WordGameApp } from '@wordgame/ui';
import styles from '../../ui/src/styles.css?inline';
import { validateApiBase } from './validate.js';

declare const __EMBED_VERSION__: string;

export interface WordGameMountOptions {
  apiBase: string;
  container: HTMLElement;
  gameId?: number;
  locale?: string;
  theme?: 'light' | 'dark';
  debug?: boolean;
}

export interface WordGameGlobal {
  mount: (options: WordGameMountOptions) => WordGameWidgetInstance;
  version: string;
}

export interface WordGameWidgetInstance {
  dispose: () => void;
}

export class WordGameElement extends HTMLElement {
  static observedAttributes = ['api-base', 'game-id', 'session-token', 'locale', 'theme'];

  private shadow: ShadowRoot;
  private app: WordGameApp | null = null;
  private mountContainer: HTMLDivElement | null = null;
  private sessionTokenAttr: string | null = null;

  constructor() {
    super();
    this.shadow = this.attachShadow({ mode: 'open' });
  }

  connectedCallback(): void {
    void this.mountWidget();
  }

  disconnectedCallback(): void {
    void this.unmountWidget(true);
  }

  attributeChangedCallback(name: string, _old: string | null, value: string | null): void {
    if (name === 'session-token') {
      this.sessionTokenAttr = value;
    }
    if (this.isConnected && (name === 'api-base' || name === 'game-id' || name === 'locale' || name === 'theme')) {
      void this.remount();
    }
  }

  private get apiBase(): string {
    return this.getAttribute('api-base') ?? '';
  }

  private get gameId(): number | undefined {
    const raw = this.getAttribute('game-id');
    if (!raw) {
      return undefined;
    }
    const id = Number.parseInt(raw, 10);
    return Number.isNaN(id) ? undefined : id;
  }

  private get locale(): string {
    return this.getAttribute('locale') ?? 'ne';
  }

  private get theme(): 'light' | 'dark' {
    const t = this.getAttribute('theme');
    return t === 'dark' ? 'dark' : 'light';
  }

  private get debug(): boolean {
    return this.hasAttribute('debug');
  }

  private dispatchWordGameEvent<T extends Record<string, unknown>>(name: string, detail: T): void {
    if (this.debug) {
      const safeDetail = { ...detail };
      delete safeDetail['sessionToken'];
      console.debug(`[word-game-widget] ${name}`, safeDetail);
    }
    this.dispatchEvent(
      new CustomEvent(name, {
        bubbles: true,
        composed: true,
        detail,
      }),
    );
  }

  private async remount(): Promise<void> {
    await this.unmountWidget(false);
    await this.mountWidget();
  }

  private async mountWidget(): Promise<void> {
    const apiBase = this.apiBase;
    if (!apiBase) {
      this.dispatchWordGameEvent('wordgame:error', {
        error: 'MISSING_API_BASE',
        message: 'api-base attribute is required.',
      });
      return;
    }

    try {
      validateApiBase(apiBase);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Invalid api-base';
      this.dispatchWordGameEvent('wordgame:error', { error: 'INVALID_API_BASE', message });
      this.renderError(message);
      return;
    }

    this.shadow.innerHTML = '';
    const style = document.createElement('style');
    style.textContent = styles;
    this.shadow.appendChild(style);

    this.mountContainer = document.createElement('div');
    this.shadow.appendChild(this.mountContainer);

    const appOptions: ConstructorParameters<typeof WordGameApp>[1] = {
      apiBase,
      locale: this.locale,
      theme: this.theme,
      debug: this.debug,
      onReady: () => {
        this.dispatchWordGameEvent('wordgame:ready', {});
      },
      onSession: (session) => {
        this.dispatchWordGameEvent('wordgame:session', {
          userId: session.userId,
          expiresAt: session.expiresAt,
        });
      },
      onGameChange: (game) => {
        this.dispatchWordGameEvent('wordgame:game', {
          gameId: game.id,
          status: game.status,
        });
      },
      onDisconnected: () => {
        this.dispatchWordGameEvent('wordgame:disconnected', {});
      },
      onReconnected: () => {
        this.dispatchWordGameEvent('wordgame:reconnected', {});
      },
      onError: (error) => {
        this.dispatchWordGameEvent('wordgame:error', {
          error: 'APP_ERROR',
          message: error.message,
        });
      },
    };
    const gameId = this.gameId;
    if (gameId !== undefined) {
      appOptions.gameId = gameId;
    }
    this.app = new WordGameApp(this.mountContainer, appOptions);

    try {
      await this.app.mount();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Mount failed';
      this.dispatchWordGameEvent('wordgame:error', { error: 'MOUNT_FAILED', message });
      this.renderError(message);
    }
  }

  private renderError(message: string): void {
    if (!this.mountContainer) {
      return;
    }
    this.mountContainer.replaceChildren();
    const root = document.createElement('div');
    root.className = 'wg-root';
    const alert = document.createElement('div');
    alert.className = 'wg-error';
    alert.setAttribute('role', 'alert');
    alert.textContent = message;
    root.appendChild(alert);
    this.mountContainer.appendChild(root);
  }

  private async unmountWidget(logout: boolean): Promise<void> {
    if (this.app) {
      this.app.unmount();
      this.app = null;
    }
    if (logout) {
      // AuthManager disposed in unmount; session cleared on widget removal
    }
    this.mountContainer = null;
  }
}

export function mount(options: WordGameMountOptions): WordGameWidgetInstance {
  validateApiBase(options.apiBase);
  const container = options.container;
  const appOptions: ConstructorParameters<typeof WordGameApp>[1] = {
    apiBase: options.apiBase,
  };
  if (options.gameId !== undefined) {
    appOptions.gameId = options.gameId;
  }
  if (options.locale !== undefined) {
    appOptions.locale = options.locale;
  }
  if (options.theme !== undefined) {
    appOptions.theme = options.theme;
  }
  if (options.debug !== undefined) {
    appOptions.debug = options.debug;
  }
  const app = new WordGameApp(container, appOptions);
  void app.mount();
  return {
    dispose: () => app.unmount(),
  };
}

export function registerWordGameElement(): void {
  if (!customElements.get('word-game-widget')) {
    customElements.define('word-game-widget', WordGameElement);
  }
}

declare global {
  interface Window {
    WordGame?: WordGameGlobal;
  }
}

export const version: string = typeof __EMBED_VERSION__ !== 'undefined' ? __EMBED_VERSION__ : '0.0.0';
