/** Matches upstream SessionJoinCode: no ambiguous 0/O/1/I/L. */
export const SESSION_JOIN_CODE_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
export const SESSION_JOIN_CODE_LENGTH = 5;

const SESSION_JOIN_CODE_PATTERN = new RegExp(
  `^[${SESSION_JOIN_CODE_ALPHABET}]{${SESSION_JOIN_CODE_LENGTH}}$`,
  'i',
);

/**
 * Normalizes a public session join code, or returns null when invalid.
 */
export function parseSessionJoinCode(input: string): string | null {
  const normalized = input.trim().toUpperCase();
  if (!SESSION_JOIN_CODE_PATTERN.test(normalized)) {
    return null;
  }
  return normalized;
}

export function isSessionJoinCode(value: string): boolean {
  return parseSessionJoinCode(value) !== null;
}
