import { describe, expect, it } from 'vitest';
import { escapeHtml } from '../src/escape.js';

describe('escapeHtml', () => {
  it('escapes script tags so they are not injectable as HTML', () => {
    const malicious = `<script>alert('xss')</script>`;
    const escaped = escapeHtml(malicious);
    expect(escaped).not.toContain('<script>');
    expect(escaped).toContain('&lt;script&gt;');
  });

  it('escapes ampersands and quotes', () => {
    expect(escapeHtml(`a & b "c"`)).toBe('a &amp; b &quot;c&quot;');
  });

  it('escapes img onerror payloads used in XSS attempts', () => {
    const malicious = `<img src=x onerror=alert(1)>`;
    const escaped = escapeHtml(malicious);
    expect(escaped).toBe('&lt;img src=x onerror=alert(1)&gt;');
    expect(escaped).not.toMatch(/<img/i);
  });
});
