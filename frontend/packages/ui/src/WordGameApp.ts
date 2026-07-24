import {
  AuthManager,
  RealtimeClient,
  isRateLimitError,
  type ApiClient,
  type Game,
  type GameChangeAction,
  type GameMember,
  type GameRealtimeMessage,
  type SessionPublic,
} from '@wordgame/sdk';
import { formatString, getStrings, type LocaleStrings } from './i18n/index.js';
import { CoalescingAsyncRunner } from './coalescingAsync.js';
import { GamePollScheduler } from './pollScheduler.js';
import {
  buildActiveGameMetaLine,
  buildFinishedGameMetaLine,
  isImpostorWordType,
} from './gameDisplay.js';
import {
  isFinishedStatus,
  isLobbyStatus,
  isPlayingStatus,
  isUnchangedGameState,
  isVotingStatus,
  resolveCurrentTurnUserId,
  sanitizeGame,
  gameStateFingerprint,
} from './sanitize.js';
import {
  getVoteCandidates,
  hasMemberVoted,
  isVoteSelectableMember,
  shouldShowWaitingForVotes,
} from './voting.js';
import { canKickMember, isMemberOffline, KICK_MIN_MEMBERS } from './kick.js';
import { clearActiveGame, readActiveGame, writeActiveGame } from './activeGame.js';

export interface WordGameAppOptions {
  apiBase: string;
  gameId?: number;
  locale?: string;
  theme?: 'light' | 'dark';
  debug?: boolean;
  onReady?: () => void;
  onSession?: (session: SessionPublic) => void;
  onGameChange?: (game: Game) => void;
  onDisconnected?: () => void;
  onReconnected?: () => void;
  onError?: (error: Error) => void;
}

type Screen = 'home' | 'waiting' | 'authenticating' | 'game' | 'error';
type HomeView = 'tiles' | 'join';
type AuthPurpose = 'start' | 'join';

const MIN_PLAYERS_TO_START = 3;
const PLAYER_NAME_STORAGE_KEY = 'wordgame:playerName';
const MAX_PLAYER_NAME_LENGTH = 30;

function loadStoredPlayerName(): string {
  try {
    return sessionStorage.getItem(PLAYER_NAME_STORAGE_KEY) ?? '';
  } catch {
    return '';
  }
}

function storePlayerName(name: string): void {
  try {
    const trimmed = name.trim();
    if (trimmed) {
      sessionStorage.setItem(PLAYER_NAME_STORAGE_KEY, trimmed);
    } else {
      sessionStorage.removeItem(PLAYER_NAME_STORAGE_KEY);
    }
  } catch {
    // sessionStorage unavailable
  }
}

export class WordGameApp {
  private readonly container: HTMLElement;
  private readonly options: WordGameAppOptions;
  private readonly strings: LocaleStrings;
  private readonly pollScheduler: GamePollScheduler;
  private auth: AuthManager;
  private realtime: RealtimeClient | null = null;
  private game: Game | null = null;
  private myWord: string | null = null;
  private myWordType: string | null = null;
  private revealedAuthenticWord: string | null = null;
  private revealedImposedWord: string | null = null;
  private wordPairFetchAttempted = false;
  private userId: string | null = null;
  private screen: Screen = 'home';
  private homeView: HomeView = 'tiles';
  private authPurpose: AuthPurpose | null = null;
  private loading = false;
  private error: { message: string; retryable: boolean } | null = null;
  private joinError: string | null = null;
  private copiedGameId = false;
  private powIterations = 0;
  private joinGameIdInput = '';
  private playerNameInput = loadStoredPlayerName();
  private pendingVoteUserId: string | null = null;
  private liveRegion: HTMLElement | null = null;
  private lastGameFingerprint: string | null = null;
  private realtimeConnected = false;
  private readonly gameRefresh = new CoalescingAsyncRunner<GameChangeAction>();
  private disposed = false;

  constructor(container: HTMLElement, options: WordGameAppOptions) {
    this.container = container;
    this.options = options;
    this.strings = getStrings(options.locale ?? 'en');
    if (options.theme) {
      this.container.dataset.theme = options.theme;
    }

    this.pollScheduler = new GamePollScheduler({
      onTick: () => {
        void this.refreshGameFromServer();
      },
    });

    this.auth = new AuthManager(options.apiBase, {
      onProgress: (p) => {
        this.powIterations = p.iterations;
        if (this.screen === 'authenticating') {
          this.render();
        }
      },
      onSession: (session) => {
        this.userId = session.userId;
        this.options.onSession?.(session);
      },
      onError: (err) => this.handleError(err),
    });
  }

  async mount(): Promise<void> {
    this.syncUserId();
    this.screen = 'home';
    this.render();
    this.options.onReady?.();

    if (this.options.gameId) {
      await this.resumeOrJoinGame(this.options.gameId);
      return;
    }

    await this.tryResumeActiveGame();
  }

  unmount(): void {
    this.disposed = true;
    this.pollScheduler.stop();
    void this.realtime?.dispose();
    this.realtime = null;
    this.lastGameFingerprint = null;
    void this.auth.logout();
    this.auth.dispose();
    this.container.innerHTML = '';
  }

  private syncUserId(): void {
    this.userId = this.auth.getSession()?.userId ?? this.userId;
  }

  private getCurrentUserId(): string | null {
    return this.userId ?? this.auth.getSession()?.userId ?? null;
  }

  private isGameAdmin(game: Game): boolean {
    const userId = this.getCurrentUserId();
    if (!userId) {
      return false;
    }
    if (game.adminUserId === userId) {
      return true;
    }
    return (game.members ?? []).some((m) => m.userId === userId && m.role === 'ADMIN');
  }

  private getOrderedMembers(members: GameMember[]): GameMember[] {
    return [...members].sort((a, b) => (a.id ?? 0) - (b.id ?? 0));
  }

  private buildPlayerNumberMap(members: GameMember[]): Map<string, number> {
    const map = new Map<string, number>();
    this.getOrderedMembers(members).forEach((member, index) => {
      map.set(member.userId, index + 1);
    });
    return map;
  }

  private formatPlayerLabel(userId: string, members: GameMember[]): string {
    const member = members.find((m) => m.userId === userId);
    const playerNumber = this.buildPlayerNumberMap(members).get(userId) ?? '?';
    const customName = member?.displayName?.trim();
    const parts = [
      customName || formatString(this.strings.playerNumber, { number: playerNumber }),
    ];
    if (member?.role === 'ADMIN') {
      parts.push(`(${this.strings.admin})`);
    }
    if (userId === this.getCurrentUserId()) {
      parts.push(`(${this.strings.you})`);
    }
    return parts.join(' ');
  }

  private resolveDisplayName(): string | undefined {
    const trimmed = this.playerNameInput.trim().slice(0, MAX_PLAYER_NAME_LENGTH);
    return trimmed.length > 0 ? trimmed : undefined;
  }

  private renderPlayerNameField(autofocus = false): string {
    return `
      <div class="wg-name-field">
        <label class="wg-label" for="wg-player-name">${this.strings.yourName}</label>
        <input
          id="wg-player-name"
          class="wg-input"
          type="text"
          maxlength="${MAX_PLAYER_NAME_LENGTH}"
          placeholder="${this.escapeAttr(this.strings.yourNamePlaceholder)}"
          value="${this.escapeAttr(this.playerNameInput)}"
          ${this.loading ? 'disabled' : ''}
          ${autofocus ? 'data-autofocus' : ''}
        />
        <p class="wg-muted wg-name-hint">${this.strings.yourNameHint}</p>
      </div>
    `;
  }

  private renderMemberLabel(userId: string, members: GameMember[]): string {
    const label = this.escapeHtml(this.formatPlayerLabel(userId, members));
    if (userId === this.getCurrentUserId()) {
      return `<span class="wg-member-you">${label}</span>`;
    }
    return label;
  }

  private renderMemberBadges(
    member: GameMember,
    options: {
      showActiveTurn: boolean;
      activeTurnUserId: string | null;
      confirmPending: boolean;
      showOffline?: boolean;
      showImpostor?: boolean;
    },
  ): string {
    const badges: string[] = [];
    if (member.eliminated) {
      badges.push(`<span class="wg-badge">${this.strings.eliminated}</span>`);
    }
    if (
      options.showActiveTurn &&
      options.activeTurnUserId === member.userId &&
      !member.eliminated
    ) {
      badges.push(`<span class="wg-badge wg-badge--active">${this.strings.activeTurn}</span>`);
    }
    if (options.confirmPending) {
      badges.push(`<span class="wg-badge wg-badge--pick">${this.strings.voteYourPick}</span>`);
    }
    if (options.showOffline && isMemberOffline(member)) {
      badges.push(`<span class="wg-badge wg-badge--offline">${this.strings.memberOffline}</span>`);
    }
    if (options.showImpostor) {
      badges.push(`<span class="wg-badge wg-badge--impostor">${this.strings.impostor}</span>`);
    }
    return badges.join('');
  }

  private renderMemberRow(
    member: GameMember,
    members: GameMember[],
    options: {
      votingMode: boolean;
      showVoteButton: boolean;
      confirmPending: boolean;
      showActiveTurn: boolean;
      activeTurnUserId: string | null;
      showKickButton: boolean;
      showOffline: boolean;
      showImpostor?: boolean;
    },
  ): string {
    const label = this.renderMemberLabel(member.userId, members);
    const badges = this.renderMemberBadges(member, {
      showActiveTurn: options.showActiveTurn,
      activeTurnUserId: options.activeTurnUserId,
      confirmPending: options.confirmPending,
      showOffline: options.showOffline,
      showImpostor: options.showImpostor === true,
    });
    const badgeHtml = badges ? ` ${badges}` : '';

    if (options.showKickButton) {
      const playerLabel = this.formatPlayerLabel(member.userId, members);
      return `
        <li class="wg-member-row wg-member-row--kickable">
          <span class="wg-member-row__label">${label}${badgeHtml}</span>
          <button
            type="button"
            class="wg-btn wg-btn--icon wg-kick-row-btn"
            data-action="kick-player"
            data-user-id="${this.escapeAttr(member.userId)}"
            aria-label="${this.escapeAttr(formatString(this.strings.kickPlayer, { player: playerLabel }))}"
            ${this.loading ? 'disabled' : ''}
          >${this.strings.kick}</button>
        </li>
      `;
    }

    if (!options.votingMode) {
      return `
        <li class="wg-member-row${options.showActiveTurn && options.activeTurnUserId === member.userId ? ' wg-member-row--active-turn' : ''}">
          <span class="wg-member-row__label">${label}${badgeHtml}</span>
        </li>
      `;
    }

    if (options.showVoteButton) {
      const playerLabel = this.formatPlayerLabel(member.userId, members);
      const voteDisabled = this.loading || (this.pendingVoteUserId != null && !options.confirmPending);
      return `
        <li class="wg-member-row wg-member-row--votable${options.confirmPending ? ' wg-member-row--pending' : ''}">
          <span class="wg-member-row__label">${label}${badgeHtml}</span>
          ${options.confirmPending ? '' : `
          <button
            type="button"
            class="wg-btn wg-btn--icon wg-vote-row-btn"
            data-action="pick-vote"
            data-user-id="${this.escapeAttr(member.userId)}"
            aria-label="${this.escapeAttr(formatString(this.strings.voteForPlayer, { player: playerLabel }))}"
            ${voteDisabled ? 'disabled' : ''}
          >${this.strings.vote}</button>
          `}
        </li>
      `;
    }

    return `
      <li class="wg-member-row wg-member-row--static">
        <span class="wg-member-row__label">${label}${badgeHtml}</span>
      </li>
    `;
  }

  private renderVoteConfirmPanel(members: GameMember[]): string {
    if (!this.pendingVoteUserId) {
      return '';
    }
    const playerLabel = this.formatPlayerLabel(this.pendingVoteUserId, members);
    return `
      <div
        class="wg-vote-confirm"
        role="dialog"
        aria-label="${this.escapeAttr(formatString(this.strings.voteForPlayer, { player: playerLabel }))}"
      >
        <div class="wg-vote-confirm__actions">
          <button type="button" class="wg-btn wg-btn--icon wg-vote-confirm__confirm" data-action="confirm-vote" aria-label="${this.escapeAttr(this.strings.confirmVoteAria)}" ${this.loading ? 'disabled' : ''}>
            ${this.strings.confirmVote}
          </button>
          <button type="button" class="wg-btn wg-btn--icon wg-btn-secondary wg-vote-confirm__cancel" data-action="cancel-vote" aria-label="${this.escapeAttr(this.strings.cancelVoteAria)}" ${this.loading ? 'disabled' : ''}>
            ${this.strings.cancelVote}
          </button>
        </div>
      </div>
    `;
  }

  private pickVoteTarget(userId: string): void {
    const members = this.game?.members ?? [];
    const member = members.find((m) => m.userId === userId);
    if (!member || !isVoteSelectableMember(member, this.getCurrentUserId())) {
      return;
    }
    this.pendingVoteUserId = userId;
    this.render();
  }

  private cancelPendingVote(): void {
    this.pendingVoteUserId = null;
    this.render();
  }

  private submitPendingVote(): void {
    if (!this.pendingVoteUserId || !this.game?.id) {
      return;
    }
    const members = this.game.members ?? [];
    const member = members.find((m) => m.userId === this.pendingVoteUserId);
    if (!member || !isVoteSelectableMember(member, this.getCurrentUserId())) {
      this.pendingVoteUserId = null;
      this.render();
      return;
    }
    const votedForUserId = this.pendingVoteUserId;
    void this.withLoading(async (api: ApiClient) => {
      await api.vote(this.game!.id!, { votedUserId: votedForUserId });
      this.pendingVoteUserId = null;
      await this.refreshGameFromServer('vote');
    });
  }

  private handleError(err: Error): void {
    const retryable = isRateLimitError(err) || ('retryable' in err && (err as { retryable: boolean }).retryable);
    let message = err.message;
    if (isRateLimitError(err)) {
      message = formatString(this.strings.rateLimited, { seconds: err.retryAfterSeconds });
    }
    this.error = { message, retryable: !!retryable };
    this.screen = 'error';
    this.loading = false;
    this.authPurpose = null;
    this.options.onError?.(err);
    this.render();
  }

  private clearError(): void {
    this.error = null;
  }

  private async ensureAuthThen(purpose: AuthPurpose, action: () => Promise<void>): Promise<void> {
    this.authPurpose = purpose;
    this.screen = 'authenticating';
    this.powIterations = 0;
    this.render();
    try {
      await this.auth.ensureAuthenticated();
      this.authPurpose = null;
      await action();
    } catch (err) {
      this.authPurpose = null;
      this.handleError(err instanceof Error ? err : new Error(String(err)));
    }
  }

  private async withLoading<T>(fn: (api: ApiClient) => Promise<T>): Promise<T | undefined> {
    if (this.loading) {
      return undefined;
    }
    this.loading = true;
    this.clearError();
    this.joinError = null;
    this.render();
    try {
      return await this.auth.wrapApiCall((api) => fn(api));
    } catch (err) {
      this.handleError(err instanceof Error ? err : new Error(String(err)));
      return undefined;
    } finally {
      this.loading = false;
      this.render();
    }
  }

  /** Loading without surfacing errors — used for silent refresh resume. */
  private async withSilentLoading<T>(fn: (api: ApiClient) => Promise<T>): Promise<T | undefined> {
    if (this.loading) {
      return undefined;
    }
    this.loading = true;
    this.render();
    try {
      return await this.auth.wrapApiCall((api) => fn(api));
    } catch {
      return undefined;
    } finally {
      this.loading = false;
      this.render();
    }
  }

  private isCurrentUserMember(game: Game): boolean {
    const userId = this.getCurrentUserId();
    if (!userId) {
      return false;
    }
    return (game.members ?? []).some((m) => m.userId === userId);
  }

  private async tryResumeActiveGame(): Promise<void> {
    const stored = readActiveGame(this.options.apiBase);
    if (!stored) {
      return;
    }

    try {
      await this.auth.ensureAuthenticated();
    } catch {
      return;
    }

    this.syncUserId();
    const userId = this.getCurrentUserId();
    if (!userId || stored.userId !== userId) {
      clearActiveGame(this.options.apiBase);
      return;
    }

    const result = await this.withSilentLoading(async (api) => {
      const game = sanitizeGame(await api.getGame(stored.gameId));
      if (!this.isCurrentUserMember(game)) {
        return null;
      }
      await this.resumeToGame(game);
      return game;
    });

    if (result === undefined || result === null) {
      clearActiveGame(this.options.apiBase);
    }
  }

  private async resumeOrJoinGame(gameId: number): Promise<void> {
    try {
      await this.auth.ensureAuthenticated();
    } catch (err) {
      this.handleError(err instanceof Error ? err : new Error(String(err)));
      return;
    }
    this.syncUserId();

    const resumed = await this.withSilentLoading(async (api) => {
      const game = sanitizeGame(await api.getGame(gameId));
      if (!this.isCurrentUserMember(game)) {
        return false;
      }
      await this.resumeToGame(game);
      return true;
    });

    if (resumed) {
      return;
    }

    await this.ensureAuthThen('join', async () => {
      await this.joinExistingGame(gameId);
    });
  }

  private async resumeToGame(game: Game): Promise<void> {
    if (isFinishedStatus(game.status)) {
      clearActiveGame(this.options.apiBase);
      return;
    }
    if (isLobbyStatus(game.status)) {
      await this.enterWaitingRoom(game);
      return;
    }
    this.seedGame(game);
    await this.connectRealtime();
    await this.enterActiveGameScreen();
  }

  private clearGameSession(): void {
    this.pollScheduler.stop();
    void this.realtime?.dispose();
    this.realtime = null;
    this.realtimeConnected = false;
    this.game = null;
    this.myWord = null;
    this.myWordType = null;
    this.revealedAuthenticWord = null;
    this.revealedImposedWord = null;
    this.wordPairFetchAttempted = false;
    this.lastGameFingerprint = null;
    this.pendingVoteUserId = null;
  }

  private async joinExistingGame(gameId: number): Promise<void> {
    const displayName = this.resolveDisplayName();
    const result = await this.withLoading(async (api) =>
      api.joinGame(gameId, displayName ? { displayName } : {}),
    );
    if (result) {
      await this.enterWaitingRoom(result);
    }
  }

  private seedGame(game: Game): void {
    const sanitized = sanitizeGame(game);
    this.game = sanitized;
    this.lastGameFingerprint = gameStateFingerprint(sanitized);
    this.options.onGameChange?.(this.game);
    const userId = this.getCurrentUserId();
    if (sanitized.id != null && userId) {
      writeActiveGame(this.options.apiBase, { gameId: sanitized.id, userId });
    }
  }

  private async enterWaitingRoom(game: Game): Promise<void> {
    this.seedGame(game);
    this.screen = 'waiting';
    this.copiedGameId = false;
    await this.connectRealtime();
    this.render();
  }

  private async enterActiveGameScreen(): Promise<void> {
    this.pollScheduler.stop();
    this.screen = 'game';
    if (isFinishedStatus(this.game?.status)) {
      clearActiveGame(this.options.apiBase);
      await this.fetchWordPair();
    } else {
      await this.fetchMyWord();
      this.syncBackgroundPoll();
    }
    this.render();
  }

  private async fetchMyWord(): Promise<void> {
    if (!this.game?.id) {
      return;
    }
    await this.auth.wrapApiCall(async (api) => {
      const response = await api.getMyWord(this.game!.id!);
      this.myWord = response.word ?? null;
      this.myWordType = response.type ?? null;
    });
  }

  /** After finish, load both words for all members (anti-cheat). */
  private async fetchWordPair(): Promise<void> {
    if (!this.game?.id) {
      return;
    }
    this.wordPairFetchAttempted = true;
    try {
      await this.auth.wrapApiCall(async (api) => {
        const pair = await api.getWordPair(this.game!.id!);
        this.revealedAuthenticWord = pair.authentic;
        this.revealedImposedWord = pair.imposed;
      });
    } catch {
      // Reveal is best-effort; finished UI still shows impostor tag.
      this.wordPairFetchAttempted = false;
    }
  }

  private onRealtimeUnavailable(): void {
    this.realtimeConnected = false;
    this.startPollForCurrentScreen();
  }

  private onRealtimeAvailable(): void {
    this.realtimeConnected = true;
    this.syncBackgroundPoll();
    void this.refreshGameFromServer();
  }

  private handleGameChanged(message?: GameRealtimeMessage): void {
    void this.refreshGameFromServer(message?.action);
  }

  private async connectRealtime(): Promise<void> {
    if (!this.game?.id) {
      return;
    }
    void this.realtime?.dispose();
    this.realtime = null;
    this.realtimeConnected = false;
    this.realtime = new RealtimeClient(
      this.options.apiBase,
      this.game.id,
      () => this.auth.getToken(),
      {
        onNotify: (message) => {
          this.handleGameChanged(message);
        },
        onReconnecting: () => {
          this.onRealtimeUnavailable();
          void this.refreshGameFromServer();
        },
        onDisconnected: () => {
          this.onRealtimeUnavailable();
          this.announce(this.strings.disconnected);
          this.options.onDisconnected?.();
        },
        onReconnected: () => {
          this.onRealtimeAvailable();
          this.announce(this.strings.reconnected);
          this.options.onReconnected?.();
        },
      },
    );
    try {
      await this.realtime.connect();
      this.onRealtimeAvailable();
    } catch {
      this.onRealtimeUnavailable();
    }
  }

  private shouldPollGameScreen(): boolean {
    const status = this.game?.status;
    if (!status) {
      return false;
    }
    return isVotingStatus(status) || isPlayingStatus(status) || isFinishedStatus(status);
  }

  /** REST poll in waiting/game: fast when SignalR is down, slower safety net when connected. */
  private syncBackgroundPoll(): void {
    if (this.screen !== 'waiting' && (this.screen !== 'game' || !this.shouldPollGameScreen())) {
      this.pollScheduler.stop();
      return;
    }
    this.pollScheduler.startForScreen(this.screen, this.realtimeConnected);
  }

  private announce(text: string): void {
    if (this.liveRegion) {
      this.liveRegion.textContent = text;
    }
  }

  private focusMain(): void {
    const focusable = this.container.querySelector<HTMLElement>('[data-autofocus]');
    focusable?.focus();
  }

  private parseGameId(input: string): number | null {
    const trimmed = input.trim();
    if (!/^\d+$/.test(trimmed)) {
      return null;
    }
    const id = Number.parseInt(trimmed, 10);
    if (Number.isNaN(id) || id <= 0) {
      return null;
    }
    return id;
  }

  private async copyGameId(): Promise<void> {
    if (!this.game?.id) {
      return;
    }
    try {
      await navigator.clipboard.writeText(String(this.game.id));
      this.copiedGameId = true;
      this.render();
      setTimeout(() => {
        if (!this.disposed && this.screen === 'waiting') {
          this.copiedGameId = false;
          this.render();
        }
      }, 2000);
    } catch {
      // Clipboard unavailable — no-op
    }
  }

  private async leaveWaitingRoom(): Promise<void> {
    clearActiveGame(this.options.apiBase);
    await this.withLoading(async (api) => {
      const userId = this.getCurrentUserId();
      if (this.game?.id && userId) {
        await api.removeGameMember(this.game.id, userId);
      }
      this.clearGameSession();
      this.screen = 'home';
      this.homeView = 'tiles';
      return undefined;
    });
  }

  private applyGameUpdate(game: Game): void {
    this.syncUserId();
    const sanitized = sanitizeGame(game);
    const previousGame = this.game;
    if (isUnchangedGameState(previousGame, sanitized, this.lastGameFingerprint)) {
      return;
    }
    this.lastGameFingerprint = gameStateFingerprint(sanitized);
    const previousCount = previousGame?.members?.length ?? 0;
    const previousStatus = previousGame?.status;
    const previousVoteResetCount = previousGame?.voteResetCount ?? 0;
    const nextVoteResetCount = sanitized.voteResetCount ?? 0;
    if (nextVoteResetCount !== previousVoteResetCount) {
      this.pendingVoteUserId = null;
    }
    this.game = sanitized;
    this.options.onGameChange?.(this.game);

    if (this.screen === 'waiting' && !isLobbyStatus(this.game.status)) {
      void this.enterActiveGameScreen();
      return;
    }

    if (this.screen === 'game') {
      if (previousStatus !== this.game.status) {
        if (isFinishedStatus(this.game.status)) {
          this.pollScheduler.stop();
          clearActiveGame(this.options.apiBase);
          void this.fetchWordPair().then(() => {
            if (!this.disposed) {
              this.render();
            }
          });
        } else {
          void this.fetchMyWord();
        }
      } else if (
        isFinishedStatus(this.game.status)
        && !this.wordPairFetchAttempted
        && !this.revealedAuthenticWord
        && !this.revealedImposedWord
      ) {
        // Retry once if an earlier fetch failed (e.g. upstream not yet exposing the pair).
        void this.fetchWordPair().then(() => {
          if (!this.disposed && (this.revealedAuthenticWord || this.revealedImposedWord)) {
            this.render();
          }
        });
      }
    }

    const newCount = this.game.members?.length ?? 0;
    if (newCount !== previousCount || previousStatus !== this.game.status) {
      this.announce(this.screen === 'waiting' ? this.strings.waitingRoom : this.strings.inGame);
    }

    this.render();
  }

  /** Single path for loading authoritative game state from the BFF. */
  private refreshGameFromServer(action?: GameChangeAction): Promise<void> {
    return this.gameRefresh.run((queuedAction) => this.fetchAndApplyGame(queuedAction), action);
  }

  private async fetchAndApplyGame(action?: GameChangeAction): Promise<void> {
    if (this.disposed || !this.game?.id) {
      return;
    }
    if (this.screen !== 'waiting' && this.screen !== 'game') {
      return;
    }

    try {
      const game = await this.auth.wrapApiCall((api) => api.getGame(this.game!.id!));
      this.applyGameUpdate(game);
      if (this.shouldFetchMyWordForAction(action, game)) {
        await this.fetchMyWord();
        this.render();
      }
    } catch {
      // Background refresh — ignore transient failures.
    }
  }

  private shouldFetchMyWordForAction(action: GameChangeAction | undefined, game: Game): boolean {
    if (!action || this.screen !== 'game') {
      return false;
    }
    if (isLobbyStatus(game.status) || isFinishedStatus(game.status)) {
      return false;
    }
    return action === 'start' || action === 'turnComplete';
  }

  private formatOutcome(outcome: string): string {
    if (outcome === 'IMPOSTOR_IDENTIFIED') {
      return this.strings.outcomeImpostorIdentified;
    }
    if (outcome === 'IMPOSTOR_SURVIVED') {
      return this.strings.outcomeImpostorSurvived;
    }
    return outcome;
  }

  private getStatusAriaLabel(status?: string): string {
    if (isLobbyStatus(status)) {
      return this.strings.statusAriaWaiting;
    }
    if (isPlayingStatus(status)) {
      return this.strings.statusAriaPlaying;
    }
    if (isVotingStatus(status)) {
      return this.strings.statusAriaVoting;
    }
    if (isFinishedStatus(status)) {
      return this.strings.statusAriaFinished;
    }
    return status ?? '';
  }

  private renderGameHeader(game: Game): string {
    const isFinished = isFinishedStatus(game.status);
    if (isFinished && game.outcome) {
      const finished = buildFinishedGameMetaLine(game, this.formatOutcome(game.outcome));
      return `
        <header class="wg-game-header">
          <h1 class="wg-title wg-game-header__title" data-autofocus tabindex="-1">${this.escapeHtml(game.name)}</h1>
          <p class="wg-game-meta wg-game-meta--outcome">
            <span class="wg-game-meta__emoji" aria-hidden="true">${finished.emoji}</span>
            <span class="wg-game-meta__text">${this.escapeHtml(finished.text)}</span>
          </p>
        </header>
      `;
    }

    const meta = buildActiveGameMetaLine(game);
    const partsHtml = meta.parts
      .map((part) => `<span class="wg-game-meta__part">${this.escapeHtml(part)}</span>`)
      .join('');
    return `
      <header class="wg-game-header">
        <h1 class="wg-title wg-game-header__title" data-autofocus tabindex="-1">${this.escapeHtml(game.name)}</h1>
        <p class="wg-game-meta" aria-label="${this.escapeAttr(this.getStatusAriaLabel(game.status))}">
          <span class="wg-game-meta__emoji" aria-hidden="true">${meta.emoji}</span>
          ${partsHtml}
        </p>
      </header>
    `;
  }

  private renderGameActions(options: {
    isLobby: boolean;
    isAdmin: boolean;
    isFinished: boolean;
    canStart: boolean;
    memberCount: number;
    canCompleteTurn: boolean;
    waitingOnVotes: boolean;
  }): string {
    const {
      isLobby,
      isAdmin,
      isFinished,
      canStart,
      memberCount,
      canCompleteTurn,
      waitingOnVotes,
    } = options;
    const content = [
      isLobby && isAdmin
        ? `<button type="button" class="wg-btn wg-btn--icon wg-btn--start" data-action="start" aria-label="${this.escapeAttr(this.strings.startGameAria)}" ${this.loading || !canStart ? 'disabled' : ''}>${this.strings.startGame}</button>${!canStart ? `<p class="wg-muted">${formatString(this.strings.needMorePlayers, { required: MIN_PLAYERS_TO_START, current: memberCount })}</p>` : ''}`
        : '',
      isLobby && !isAdmin ? `<p class="wg-muted">${this.strings.waitingForPlayers}</p>` : '',
      canCompleteTurn
        ? `<button type="button" class="wg-btn wg-btn--icon" data-action="complete-turn" aria-label="${this.escapeAttr(this.strings.completeTurnAria)}" ${this.loading ? 'disabled' : ''}>${this.strings.completeTurn}</button>`
        : '',
      waitingOnVotes ? `<p class="wg-muted">${this.strings.waitingForVotes}</p>` : '',
      isFinished
        ? `<button type="button" class="wg-btn wg-btn--icon wg-btn--back wg-btn-secondary" data-action="go-home" aria-label="${this.escapeAttr(this.strings.joinBackAria)}" ${this.loading ? 'disabled' : ''}>${this.strings.joinBack}</button>`
        : '',
    ]
      .filter(Boolean)
      .join('');

    if (!content) {
      return '';
    }
    return content.includes('<button')
      ? `<div class="wg-section">${content}</div>`
      : content;
  }

  private startPollForCurrentScreen(): void {
    if (this.screen === 'waiting' || this.screen === 'game') {
      this.pollScheduler.startForScreen(this.screen, this.realtimeConnected);
    }
  }

  private render(): void {
    if (this.disposed) {
      return;
    }

    const html = this.buildHtml();
    this.container.innerHTML = html;
    this.liveRegion = this.container.querySelector('[data-live]');
    this.bindEvents();
    this.focusMain();
  }

  private buildHtml(): string {
    if (this.screen === 'authenticating') {
      return this.renderAuthenticating();
    }
    if (this.screen === 'error') {
      return this.renderError();
    }
    if (this.screen === 'home') {
      return this.renderHome();
    }
    if (this.screen === 'waiting') {
      return this.renderWaiting();
    }
    return this.renderGame();
  }

  private renderAuthenticating(): string {
    const title = this.authPurpose ? this.strings.botVerification : this.strings.authenticating;
    return `
      <div class="wg-root" role="status">
        <h1 class="wg-title">${title}</h1>
        <p class="wg-muted">
          <span class="wg-spinner" aria-hidden="true"></span>
          ${formatString(this.strings.powProgress, { iterations: this.powIterations })}
        </p>
        <div class="wg-live" data-live aria-live="polite">${title}</div>
      </div>
    `;
  }

  private renderError(): string {
    return `
      <div class="wg-root">
        <h1 class="wg-title">${this.strings.error}</h1>
        <div class="wg-error" role="alert">${this.escapeHtml(this.error?.message ?? '')}</div>
        ${this.error?.retryable ? `<button type="button" class="wg-btn wg-btn--icon" data-action="retry" aria-label="${this.escapeAttr(this.strings.retryAria)}" data-autofocus>${this.strings.retry}</button>` : ''}
        <button type="button" class="wg-btn wg-btn--icon wg-btn--back wg-btn-secondary" data-action="go-home" aria-label="${this.escapeAttr(this.strings.joinBackAria)}">${this.strings.joinBack}</button>
        <div class="wg-live" data-live aria-live="polite">${this.escapeHtml(this.error?.message ?? '')}</div>
      </div>
    `;
  }

  private renderHome(): string {
    const joinPanel =
      this.homeView === 'join'
        ? `
        <div class="wg-join-panel">
          <label class="wg-label" for="wg-join-id">${this.strings.gameId}</label>
          <input id="wg-join-id" class="wg-input" type="text" inputmode="numeric" pattern="[0-9]*" value="${this.escapeAttr(this.joinGameIdInput)}" ${this.loading ? 'disabled' : ''} />
          ${this.joinError ? `<div class="wg-error" role="alert">${this.escapeHtml(this.joinError)}</div>` : ''}
          <div class="wg-join-actions">
            <button type="button" class="wg-btn wg-btn--icon wg-btn--join" data-action="join-submit" aria-label="${this.escapeAttr(this.strings.joinSubmitAria)}" ${this.loading ? 'disabled' : ''}>${this.strings.joinSubmit}</button>
            <button type="button" class="wg-btn wg-btn--icon wg-btn--back wg-btn-secondary" data-action="join-back" aria-label="${this.escapeAttr(this.strings.joinBackAria)}" ${this.loading ? 'disabled' : ''}>${this.strings.joinBack}</button>
          </div>
        </div>
      `
        : `
        <div class="wg-tile-grid">
          <button type="button" class="wg-tile" data-action="start-game" ${this.loading ? 'disabled' : ''}>
            <span class="wg-tile-title">${this.strings.tileStartTitle}</span>
            <span class="wg-tile-hint">${this.strings.tileStartHint}</span>
          </button>
          <button type="button" class="wg-tile" data-action="show-join" ${this.loading ? 'disabled' : ''}>
            <span class="wg-tile-title">${this.strings.tileJoinTitle}</span>
            <span class="wg-tile-hint">${this.strings.tileJoinHint}</span>
          </button>
        </div>
      `;

    return `
      <div class="wg-root">
        <h1 class="wg-title" tabindex="-1">${this.strings.homeTitle}</h1>
        <p class="wg-intro">${this.strings.gameIntro}</p>
        ${this.renderPlayerNameField(this.homeView === 'tiles')}
        ${joinPanel}
        ${this.loading ? `<p class="wg-muted"><span class="wg-spinner"></span>${this.strings.loading}</p>` : ''}
        <div class="wg-live" data-live aria-live="polite">${this.strings.homeTitle}</div>
      </div>
    `;
  }

  private renderWaiting(): string {
    const game = this.game;
    if (!game) {
      return this.renderHome();
    }

    const members = game.members ?? [];
    const isAdmin = this.isGameAdmin(game);
    const isLobby = isLobbyStatus(game.status);
    const canStart = members.length >= MIN_PLAYERS_TO_START;
    const startDisabled = this.loading || !canStart;

    return `
      <div class="wg-root${isAdmin ? ' wg-root--admin' : ''}">
        <h1 class="wg-title">${isAdmin ? this.strings.adminWaitingRoom : this.strings.waitingRoom}</h1>
        ${isAdmin ? `<p class="wg-admin-banner">${formatString(this.strings.adminHint, { required: MIN_PLAYERS_TO_START })}</p>` : ''}
        <div class="wg-game-id-card">
          <p class="wg-label">${this.strings.shareGameId}</p>
          <p class="wg-game-id-value" data-autofocus tabindex="-1">${game.id ?? '—'}</p>
          <button type="button" class="wg-btn wg-btn--icon" data-action="copy-game-id" aria-label="${this.escapeAttr(this.strings.copyGameIdAria)}" ${this.loading ? 'disabled' : ''}>
            ${this.copiedGameId ? this.strings.copiedGameId : this.strings.copyGameId}
          </button>
        </div>
        <div class="wg-section">
          <p class="wg-label">${this.strings.members} (${members.length}/${MIN_PLAYERS_TO_START})</p>
          <ul class="wg-member-list">
            ${members.length === 0 ? `<li class="wg-muted">${this.strings.noMembers}</li>` : members.map((m) => `
              <li>
                ${this.renderMemberLabel(m.userId, members)}
              </li>
            `).join('')}
          </ul>
        </div>
        ${isLobby && isAdmin ? `
          <div class="wg-start-block">
            <button type="button" class="wg-btn wg-btn--icon wg-btn--start" data-action="start" aria-label="${this.escapeAttr(this.strings.startGameAria)}" ${startDisabled ? 'disabled' : ''}>${this.strings.startGame}</button>
            ${!canStart ? `<p class="wg-muted">${formatString(this.strings.needMorePlayers, { required: MIN_PLAYERS_TO_START, current: members.length })}</p>` : ''}
          </div>
        ` : ''}
        ${!isAdmin ? `<p class="wg-muted">${this.strings.waitingForAdmin}</p>` : ''}
        <button type="button" class="wg-btn wg-btn--icon wg-btn-secondary" data-action="leave-waiting" aria-label="${this.escapeAttr(this.strings.leaveGameAria)}" ${this.loading ? 'disabled' : ''}>${this.strings.leaveGame}</button>
        ${this.loading ? `<p class="wg-muted"><span class="wg-spinner"></span>${this.strings.loading}</p>` : ''}
        <div class="wg-live" data-live aria-live="polite">${this.strings.waitingRoom}</div>
      </div>
    `;
  }

  private renderGame(): string {
    const game = this.game;
    if (!game) {
      return this.renderHome();
    }

    const members = game.members ?? [];
    const isAdmin = this.isGameAdmin(game);
    const isLobby = isLobbyStatus(game.status);
    const isPlaying = isPlayingStatus(game.status);
    const isVoting = isVotingStatus(game.status);
    const isFinished = isFinishedStatus(game.status);
    const currentMember = members.find((m) => m.userId === this.getCurrentUserId());
    const canStart = members.length >= MIN_PLAYERS_TO_START;
    const currentTurnUserId = resolveCurrentTurnUserId(game);
    const isMyTurn = isPlaying && currentTurnUserId === this.getCurrentUserId();
    const canCompleteTurn =
      isMyTurn && currentMember && !currentMember.turnCompleted && !currentMember.eliminated;
    const voteCandidates = getVoteCandidates(members, this.getCurrentUserId());
    const selfHasVoted = hasMemberVoted(currentMember);
    const canVote = Boolean(
      isVoting &&
        currentMember &&
        !currentMember.eliminated &&
        !selfHasVoted &&
        voteCandidates.length > 0,
    );
    if (canVote) {
      const pendingStillValid =
        this.pendingVoteUserId != null &&
        voteCandidates.some((m) => m.userId === this.pendingVoteUserId);
      if (!pendingStillValid) {
        this.pendingVoteUserId = null;
      }
    }
    const waitingOnVotes = shouldShowWaitingForVotes(isVoting, currentMember);
    const showOffline = !isLobby && !isFinished;
    const canKickMembers = isAdmin && showOffline && members.length >= KICK_MIN_MEMBERS;
    const impostorUserId = isFinished ? game.impostorUserId : undefined;
    const gameActions = this.renderGameActions({
      isLobby,
      isAdmin,
      isFinished,
      canStart,
      memberCount: members.length,
      canCompleteTurn: Boolean(canCompleteTurn),
      waitingOnVotes,
    });

    return `
      <div class="wg-root">
        ${this.renderGameHeader(game)}
        ${this.myWord && !isLobby && !isFinished ? `
          <div class="wg-section wg-word-panel">
            <p class="wg-word-line">
              <span class="wg-word-line__label">${this.strings.yourWord}</span>
              <span class="wg-word-line__value">${this.escapeHtml(this.myWord)}</span>
            </p>
            ${isImpostorWordType(this.myWordType ?? undefined) ? `
              <p class="wg-impostor-hint">${this.escapeHtml(this.strings.youAreImpostor)}</p>
            ` : ''}
          </div>
        ` : ''}
        ${isFinished && (this.revealedImposedWord || this.revealedAuthenticWord) ? `
          <div class="wg-section wg-word-panel">
            ${this.revealedImposedWord ? `
              <p class="wg-word-line">
                <span class="wg-word-line__label">${this.strings.impostorWord}</span>
                <span class="wg-word-line__value">${this.escapeHtml(this.revealedImposedWord)}</span>
              </p>
            ` : ''}
            ${this.revealedAuthenticWord ? `
              <p class="wg-word-line">
                <span class="wg-word-line__label">${this.strings.crewWord}</span>
                <span class="wg-word-line__value">${this.escapeHtml(this.revealedAuthenticWord)}</span>
              </p>
            ` : ''}
          </div>
        ` : ''}
        <div class="wg-section${canVote ? ' wg-vote-panel' : ''}">
          <p class="wg-label">${this.strings.members}</p>
          ${canVote && (game.voteResetCount ?? 0) > 0 ? `<p class="wg-muted">${this.strings.voteTieHint}</p>` : ''}
          <ul class="wg-member-list${canVote ? ' wg-member-list--voting' : ''}">
            ${members.length === 0 ? `<li class="wg-muted">${this.strings.noMembers}</li>` : members.map((m) =>
              this.renderMemberRow(m, members, {
                votingMode: canVote,
                showVoteButton: canVote && isVoteSelectableMember(m, this.getCurrentUserId()),
                confirmPending: m.userId === this.pendingVoteUserId,
                showActiveTurn: isPlaying,
                activeTurnUserId: currentTurnUserId,
                showKickButton: canKickMembers && canKickMember(m, game, this.getCurrentUserId()),
                showOffline,
                showImpostor: Boolean(impostorUserId && m.userId === impostorUserId),
              }),
            ).join('')}
          </ul>
          ${canVote ? this.renderVoteConfirmPanel(members) : ''}
        </div>
        ${gameActions}
        ${this.loading ? `<p class="wg-muted"><span class="wg-spinner"></span>${this.strings.loading}</p>` : ''}
        <div class="wg-live" data-live aria-live="polite">${this.escapeHtml(game.status ?? '')}</div>
      </div>
    `;
  }

  private bindEvents(): void {
    this.container.querySelector('[data-action="retry"]')?.addEventListener('click', () => {
      this.clearError();
      this.screen = 'home';
      this.render();
    });

    this.container.querySelector('[data-action="go-home"]')?.addEventListener('click', () => {
      this.clearError();
      clearActiveGame(this.options.apiBase);
      this.clearGameSession();
      this.screen = 'home';
      this.homeView = 'tiles';
      this.render();
    });

    const joinIdInput = this.container.querySelector<HTMLInputElement>('#wg-join-id');
    joinIdInput?.addEventListener('input', (e) => {
      this.joinGameIdInput = (e.target as HTMLInputElement).value;
      this.joinError = null;
    });

    this.container.querySelector<HTMLInputElement>('#wg-player-name')?.addEventListener('input', (e) => {
      this.playerNameInput = (e.target as HTMLInputElement).value;
      storePlayerName(this.playerNameInput);
    });

    this.container.querySelectorAll<HTMLButtonElement>('[data-action="pick-vote"]').forEach((el) => {
      el.addEventListener('click', () => {
        const userId = el.dataset.userId;
        if (userId) {
          this.pickVoteTarget(userId);
        }
      });
    });

    this.container.querySelector('[data-action="confirm-vote"]')?.addEventListener('click', () => {
      this.submitPendingVote();
    });

    this.container.querySelector('[data-action="cancel-vote"]')?.addEventListener('click', () => {
      this.cancelPendingVote();
    });

    this.container.querySelector('[data-action="show-join"]')?.addEventListener('click', () => {
      this.homeView = 'join';
      this.joinError = null;
      this.render();
    });

    this.container.querySelector('[data-action="join-back"]')?.addEventListener('click', () => {
      this.homeView = 'tiles';
      this.joinError = null;
      this.joinGameIdInput = '';
      this.render();
    });

    this.container.querySelector('[data-action="start-game"]')?.addEventListener('click', () => {
      void this.ensureAuthThen('start', async () => {
        const displayName = this.resolveDisplayName();
        const result = await this.withLoading(async (api) =>
          api.createGame({
            name: this.strings.defaultGameName,
            ...(displayName ? { displayName } : {}),
          }),
        );
        if (result) {
          await this.enterWaitingRoom(result);
        }
      });
    });

    this.container.querySelector('[data-action="join-submit"]')?.addEventListener('click', () => {
      const gameId = this.parseGameId(this.joinGameIdInput);
      if (gameId === null) {
        this.joinError = this.strings.invalidGameId;
        this.render();
        return;
      }
      void this.ensureAuthThen('join', async () => {
        await this.joinExistingGame(gameId);
      });
    });

    this.container.querySelector('[data-action="copy-game-id"]')?.addEventListener('click', () => {
      void this.copyGameId();
    });

    this.container.querySelector('[data-action="leave-waiting"]')?.addEventListener('click', () => {
      void this.leaveWaitingRoom();
    });

    this.container.querySelector('[data-action="start"]')?.addEventListener('click', () => {
      void this.withLoading(async (api: ApiClient) => {
        if (!this.game?.id) {
          return;
        }
        const secretWord = await api.getRandomSecretWord(this.game.id);
        await api.startGame(this.game.id, {
          secretWordId: secretWord.id!,
        });
        await this.refreshGameFromServer('start');
      });
    });

    this.container.querySelectorAll<HTMLButtonElement>('[data-action="kick-player"]').forEach((el) => {
      el.addEventListener('click', () => {
        const userId = el.dataset.userId;
        if (userId) {
          void this.kickMember(userId);
        }
      });
    });

    this.container.querySelector('[data-action="complete-turn"]')?.addEventListener('click', () => {
      void this.withLoading(async (api: ApiClient) => {
        if (!this.game?.id) {
          return;
        }
        await api.completeTurn(this.game.id);
        await this.refreshGameFromServer('turnComplete');
      });
    });
  }

  private kickMember(userId: string): void {
    if (!this.game?.id) {
      return;
    }
    const members = this.game.members ?? [];
    const member = members.find((m) => m.userId === userId);
    if (!member || !canKickMember(member, this.game, this.getCurrentUserId())) {
      return;
    }
    void this.withLoading(async (api: ApiClient) => {
      await api.removeGameMember(this.game!.id!, userId);
      await this.refreshGameFromServer('memberRemoved');
    });
  }

  private escapeHtml(text: string): string {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  private escapeAttr(text: string): string {
    return this.escapeHtml(text).replace(/'/g, '&#39;');
  }
}
