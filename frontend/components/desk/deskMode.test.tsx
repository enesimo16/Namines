import { describe, it, expect } from 'vitest';
import { resolveDeskMode } from './deskMode';

describe('resolveDeskMode', () => {
  it('oturum açık + saklı bağlantı → canlı', () => {
    expect(resolveDeskMode(true, { hasConnection: true })).toEqual({ kind: 'live' });
  });
  it('oturum yok → örnek (signed-out), bağlantı olsa bile', () => {
    expect(resolveDeskMode(false, { hasConnection: true })).toEqual({ kind: 'sample', reason: 'signed-out' });
  });
  it('bağlantı yok ya da DTO yok → örnek (no-connection)', () => {
    expect(resolveDeskMode(true, { hasConnection: false })).toEqual({ kind: 'sample', reason: 'no-connection' });
    expect(resolveDeskMode(true, null)).toEqual({ kind: 'sample', reason: 'no-connection' });
  });
});
