import { describe, it, expect } from 'vitest';
import { normalizeApiBase } from './config';

describe('normalizeApiBase', () => {
  it('strips a trailing /api and slashes so calls never become /api/api', () => {
    expect(normalizeApiBase('https://x.example/api')).toBe('https://x.example');
    expect(normalizeApiBase('https://x.example/api/')).toBe('https://x.example');
    expect(normalizeApiBase('http://localhost/api')).toBe('http://localhost');
  });

  it('keeps an origin-only or empty (same-origin) value', () => {
    expect(normalizeApiBase('http://namines-backend:8080')).toBe('http://namines-backend:8080');
    expect(normalizeApiBase('')).toBe('');
  });
});
