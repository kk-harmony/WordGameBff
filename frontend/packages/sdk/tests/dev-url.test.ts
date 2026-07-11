import { describe, expect, it } from 'vitest';
import { resolveDevApiBase } from '../src/dev-url.js';

describe('resolveDevApiBase', () => {
  it('returns override from query string', () => {
    expect(
      resolveDevApiBase('http://localhost:8080', {
        search: '?apiBase=http://192.168.0.9:9000',
        hostname: 'localhost',
      }),
    ).toBe('http://192.168.0.9:9000');
  });

  it('rewrites localhost fallback to page hostname on LAN', () => {
    expect(
      resolveDevApiBase('http://localhost:8080', {
        search: '',
        hostname: '192.168.0.17',
      }),
    ).toBe('http://192.168.0.17:8080');
  });

  it('keeps localhost fallback on localhost', () => {
    expect(
      resolveDevApiBase('http://localhost:8080', {
        search: '',
        hostname: 'localhost',
      }),
    ).toBe('http://localhost:8080');
  });
});
