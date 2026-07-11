import { describe, expect, it } from 'vitest';
import { validateApiBase } from '../src/validate.js';

describe('validateApiBase', () => {
  it('accepts https production URLs', () => {
    expect(() => validateApiBase('https://bff.example.com')).not.toThrow();
  });

  it('accepts http localhost', () => {
    expect(() => validateApiBase('http://localhost:8080')).not.toThrow();
    expect(() => validateApiBase('http://127.0.0.1:8080')).not.toThrow();
  });

  it('accepts http private network hosts', () => {
    expect(() => validateApiBase('http://192.168.1.42:8080')).not.toThrow();
    expect(() => validateApiBase('http://10.0.0.5:8080')).not.toThrow();
    expect(() => validateApiBase('http://172.16.0.1:8080')).not.toThrow();
  });

  it('accepts http mDNS .local hosts', () => {
    expect(() => validateApiBase('http://my-mac.local:8080')).not.toThrow();
  });

  it('rejects http public hosts', () => {
    expect(() => validateApiBase('http://example.com')).toThrow(/HTTPS/);
    expect(() => validateApiBase('http://8.8.8.8:8080')).toThrow(/HTTPS/);
  });

  it('rejects invalid URLs', () => {
    expect(() => validateApiBase('not-a-url')).toThrow(/valid URL/);
  });

  it('rejects non-http(s) schemes', () => {
    expect(() => validateApiBase('ftp://localhost:8080')).toThrow(/HTTPS/);
  });
});
