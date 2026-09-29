import { describe, expect, it } from 'vitest';
import { getStrings } from '../src/i18n/index.js';

describe('Nepali locale strings', () => {
  const ne = getStrings('ne');

  it('uses बहुरूपी for impostor wording', () => {
    expect(ne.impostor).toBe('बहुरूपी');
    expect(ne.impostorWord).toBe('बहुरूपी शब्द');
    expect(ne.youAreImpostor).toContain('बहुरूपी');
    expect(ne.gameIntro).toContain('बहुरूपी');
    expect(ne.outcomeImpostorIdentified).toContain('बहुरूपी');
    expect(ne.outcomeImpostorSurvived).toContain('बहुरूपी');
  });

  it('keeps the home intro to a single sentence', () => {
    expect(ne.gameIntro).toBe('गोप्य शब्द नजान्ने बहुरूपीलाई पत्ता लगाउनुहोस्।');
  });

  it('uses कामको प्रमाण for proof of work progress', () => {
    expect(ne.powProgress).toContain('कामको प्रमाण');
  });

  it('uses सत्र ID and प्रशासक instead of English game/admin', () => {
    expect(ne.gameId).toBe('सत्र ID');
    expect(ne.adminCreateHint).toContain('सत्र ID');
    expect(ne.shareGameId).toContain('सत्र ID');
    expect(ne.copyGameIdAria).toContain('सत्र ID');
    expect(ne.invalidGameId).toContain('सत्र ID');
    expect(ne.invalidGameId).toMatch(/५|5/);
    expect(ne.admin).toBe('प्रशासक');
    expect(ne.tabAdmin).toBe('प्रशासक');
    expect(ne.adminWaitingRoom).toContain('प्रशासक');
    expect(ne.adminHint).toContain('प्रशासक');
    expect(ne.waitingForAdmin).toContain('प्रशासक');
    expect(ne.adminCreateHint).not.toMatch(/\bgame\b/i);
    expect(ne.shareGameId).not.toMatch(/\bgame\b/i);
    expect(ne.adminHint).not.toMatch(/\badmin\b/i);
    expect(ne.waitingForAdmin).not.toMatch(/\badmin\b/i);
  });

  it('localizes session lobby progress and join hints', () => {
    expect(ne.sessionGamesProgress).toContain('{count}');
    expect(ne.sessionGamesProgress).toContain('{max}');
    expect(ne.sessionGameLimitReached).toContain('सत्र');
    expect(ne.sessionGameLimitReached).toContain('{max}');
    expect(ne.backToLobbyAria).toContain('सत्र');
    expect(ne.playerJoinHint).toContain('सत्र ID');
    expect(ne.needMorePlayers).toContain('{required}');
    expect(ne.sessionGamesProgress).not.toMatch(/\bgame\b/i);
    expect(ne.sessionGameLimitReached).not.toMatch(/\bgame\b/i);
    expect(ne.playerJoinHint).not.toMatch(/\bgame\b/i);
    expect(ne.scoreboard).toBe('अंक');
    expect(ne.points).toContain('{score}');
    expect(ne.points).not.toMatch(/\bpts\b/i);
    expect(ne.startNextGameAria).toContain('खेल');
    expect(ne.startNextGameAria).not.toMatch(/\badmin\b/i);
  });
});
