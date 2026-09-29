import { describe, expect, it } from 'vitest';
import { getStrings } from '../src/i18n/index.js';

describe('Nepali locale strings', () => {
  const ne = getStrings('ne');
  const en = getStrings('en');

  it('uses बहुरूपी for impostor wording', () => {
    expect(ne.impostor).toBe('बहुरूपी');
    expect(ne.impostorWord).toBe('बहुरूपी शब्द');
    expect(ne.youAreImpostor).toContain('बहुरूपी');
    expect(ne.gameIntro).toContain('बहुरूपी');
    expect(ne.outcomeImpostorIdentified).toContain('बहुरूपी');
    expect(ne.outcomeImpostorSurvived).toContain('बहुरूपी');
    expect(ne.rulesOverview).toContain('बहुरूपी');
    expect(ne.rulesVote).toContain('बहुरूपी');
  });

  it('keeps the home intro to a single sentence', () => {
    expect(ne.gameIntro).toBe('गोप्य शब्द नजान्ने बहुरूपीलाई पत्ता लगाउनुहोस्।');
    expect(en.gameIntro).toBe('Find the impostor who does not know the secret word.');
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

  it('uses session and lobby wording instead of room', () => {
    expect(en.createRoom).toBe('Create session');
    expect(en.waitingRoom).toBe('Session lobby');
    expect(en.adminWaitingRoom).toBe('Session lobby — Admin');
    expect(en.leaveGameAria).toBe('Leave session');
    expect(en.adminCreateHint).toContain('session');
    expect(en.adminCreateHint).not.toMatch(/\broom\b/i);
    expect(en.waitingRoom).not.toMatch(/\broom\b/i);
    expect(en.leaveGameAria).not.toMatch(/\broom\b/i);

    expect(ne.createRoom).toContain('सत्र');
    expect(ne.waitingRoom).toBe('सत्र लबी');
    expect(ne.adminWaitingRoom).toContain('सत्र लबी');
    expect(ne.leaveGameAria).toContain('सत्र');
    expect(ne.adminCreateHint).toContain('सत्र');
    expect(ne.createRoom).not.toMatch(/कोठा/);
    expect(ne.waitingRoom).not.toMatch(/कोठा/);
    expect(ne.leaveGameAria).not.toMatch(/कोठा/);
    expect(ne.adminCreateHint).not.toMatch(/कोठा/);
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

  it('provides Play and Rules app tabs with how-to copy', () => {
    expect(en.tabPlay).toBe('Play');
    expect(en.tabRules).toBe('Rules');
    expect(en.rulesTitle).toBe('How to play');
    expect(en.rulesOverview.length).toBeGreaterThan(20);
    expect(en.rulesTurns.length).toBeGreaterThan(20);
    expect(en.rulesVote.length).toBeGreaterThan(20);
    expect(en.rulesSession).toContain('Session ID');
    expect(en.rulesSession).toContain('lobby');

    expect(ne.tabPlay).toBe('खेल');
    expect(ne.tabRules).toBe('नियम');
    expect(ne.rulesTitle).toContain('खेल');
    expect(ne.rulesOverview).toContain('बहुरूपी');
    expect(ne.rulesTurns.length).toBeGreaterThan(10);
    expect(ne.rulesVote).toContain('बहुरूपी');
    expect(ne.rulesSession).toContain('सत्र');
    expect(ne.rulesSession).toContain('लबी');
    expect(ne.appTabsAria).not.toMatch(/\bplay\b/i);
    expect(ne.tabRules).not.toMatch(/\brules\b/i);
    expect(ne.rulesSession).not.toMatch(/\broom\b/i);
  });
});
