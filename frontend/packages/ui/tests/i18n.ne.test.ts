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

  it('uses खेल and प्रशासक instead of English game/admin', () => {
    expect(ne.gameId).toBe('खेल ID');
    expect(ne.tileStartHint).toContain('खेल ID');
    expect(ne.shareGameId).toContain('खेल ID');
    expect(ne.copyGameIdAria).toContain('खेल ID');
    expect(ne.invalidGameId).toContain('खेल ID');
    expect(ne.admin).toBe('प्रशासक');
    expect(ne.adminWaitingRoom).toContain('प्रशासक');
    expect(ne.adminHint).toContain('प्रशासक');
    expect(ne.waitingForAdmin).toContain('प्रशासक');
    expect(ne.tileStartHint).not.toMatch(/\bgame\b/i);
    expect(ne.shareGameId).not.toMatch(/\bgame\b/i);
    expect(ne.adminHint).not.toMatch(/\badmin\b/i);
    expect(ne.waitingForAdmin).not.toMatch(/\badmin\b/i);
  });
});
