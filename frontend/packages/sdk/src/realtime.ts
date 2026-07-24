import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type IHttpConnectionOptions,
} from '@microsoft/signalr';
import type { GameRealtimeMessage } from './types.js';

const RECEIVE_METHOD = 'gameEvent';
const RECONNECT_DELAYS = [0, 2000, 5000, 10000, 30000];

const HUB_CONNECTION_OPTIONS: IHttpConnectionOptions = {
  skipNegotiation: true,
  transport: HttpTransportType.WebSockets,
};

export interface RealtimeCallbacks {
  /** Lightweight hub notification — fetch game state via REST in the handler. */
  onNotify?: (message: GameRealtimeMessage) => void;
  onReconnecting?: () => void;
  onDisconnected?: () => void;
  onReconnected?: () => void;
}

export class RealtimeClient {
  private readonly apiBase: string;
  private readonly gameId: number;
  private readonly getToken: () => string | null;
  private readonly callbacks: RealtimeCallbacks;
  private connection: HubConnection | null = null;
  private lastRevision = 0;
  private disposed = false;

  constructor(
    apiBase: string,
    gameId: number,
    getToken: () => string | null,
    callbacks: RealtimeCallbacks = {},
  ) {
    this.apiBase = apiBase.replace(/\/$/, '');
    this.gameId = gameId;
    this.getToken = getToken;
    this.callbacks = callbacks;
  }

  getLastRevision(): number {
    return this.lastRevision;
  }

  private buildHubUrl(): string {
    const token = encodeURIComponent(this.getToken() ?? '');
    return `${this.apiBase}/hubs/game?gameId=${this.gameId}&access_token=${token}`;
  }

  async connect(): Promise<void> {
    if (this.disposed || this.connection?.state === HubConnectionState.Connected) {
      return;
    }

    this.connection?.stop().catch(() => undefined);

    this.connection = new HubConnectionBuilder()
      .withUrl(this.buildHubUrl(), HUB_CONNECTION_OPTIONS)
      .withAutomaticReconnect(RECONNECT_DELAYS)
      .configureLogging(LogLevel.Warning)
      .build();

    this.bindConnectionHandlers(this.connection);
    await this.connection.start();
  }

  private bindConnectionHandlers(connection: HubConnection): void {
    connection.on(RECEIVE_METHOD, (message: GameRealtimeMessage) => {
      this.handleMessage(message);
    });

    connection.onclose(() => this.emitIfActive(this.callbacks.onDisconnected));
    connection.onreconnecting(() => this.emitIfActive(this.callbacks.onReconnecting));
    connection.onreconnected(() => this.emitIfActive(this.callbacks.onReconnected));
  }

  private handleMessage(message: GameRealtimeMessage): void {
    if (message.revision < this.lastRevision) {
      return;
    }
    if (message.revision > this.lastRevision) {
      this.lastRevision = message.revision;
    }
    this.callbacks.onNotify?.(message);
  }

  private emitIfActive(callback?: () => void): void {
    if (!this.disposed) {
      callback?.();
    }
  }

  async disconnect(): Promise<void> {
    if (!this.connection) {
      return;
    }
    await this.connection.stop();
    this.connection = null;
  }

  dispose(): void {
    this.disposed = true;
    void this.disconnect();
  }
}

export { RECEIVE_METHOD, RECONNECT_DELAYS };
