import { describe, expect, it, vi } from 'vitest';
import { handleGameChangedNotification } from '../src/gameChangedHandler.js';
import type { Game, GameRealtimeMessage } from '@wordgame/sdk';

const sampleGame: Game = {
  id: 42,
  name: 'Room',
  adminUserId: 'admin',
  status: 'WAITING',
  members: [{ userId: 'admin', role: 'ADMIN' }],
};

describe('handleGameChangedNotification', () => {
  it('applies a pushed snapshot without refetching', () => {
    const applyGame = vi.fn();
    const refreshFromServer = vi.fn();
    const message: GameRealtimeMessage = {
      type: 'gameChanged',
      gameId: 42,
      revision: 3,
      action: 'join',
      game: sampleGame,
    };

    handleGameChangedNotification(message, { applyGame, refreshFromServer });

    expect(applyGame).toHaveBeenCalledWith(sampleGame);
    expect(refreshFromServer).not.toHaveBeenCalled();
  });

  it('refetches when the event has no snapshot', () => {
    const applyGame = vi.fn();
    const refreshFromServer = vi.fn();
    const message: GameRealtimeMessage = {
      type: 'gameChanged',
      gameId: 42,
      revision: 4,
      action: 'vote',
    };

    handleGameChangedNotification(message, { applyGame, refreshFromServer });

    expect(applyGame).not.toHaveBeenCalled();
    expect(refreshFromServer).toHaveBeenCalledWith('vote');
  });
});
