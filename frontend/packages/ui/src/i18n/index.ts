import { en, formatString, type LocaleKey, type LocaleStrings } from './en.js';
import { ne } from './ne.js';

export type { LocaleStrings, LocaleKey };
export { formatString };

export function getStrings(locale: string): LocaleStrings {
  if (locale.toLowerCase().startsWith('ne')) {
    return ne;
  }
  return en;
}
