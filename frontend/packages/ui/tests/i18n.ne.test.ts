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

  it('uses कामको प्रमाण for proof of work progress', () => {
    expect(ne.powProgress).toContain('कामको प्रमाण');
  });
});
