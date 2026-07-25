import type { Game, GameChangeAction, GameRealtimeMessage } from '@wordgame/sdk';

export interface GameChangedHandlerDeps {
  applyGame: (game: Game) => void;
  refreshFromServer: (action?: GameChangeAction) => void;
}

/** Prefer a pushed snapshot; only refetch when the event is lightweight. */
export function handleGameChangedNotification(
  message: GameRealtimeMessage | undefined,
  deps: GameChangedHandlerDeps,
): void {
  if (message?.game) {
    deps.applyGame(message.game);
    return;
  }
  deps.refreshFromServer(message?.action);
}
