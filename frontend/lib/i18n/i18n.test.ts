import { describe, it, expect } from 'vitest';
import { translate } from './index';
import en from './messages/en';
import tr from './messages/tr';

function leafKeys(node: unknown, prefix = ''): string[] {
  if (typeof node === 'string') return [prefix];
  return Object.entries(node as Record<string, unknown>)
    .flatMap(([key, value]) => leafKeys(value, prefix ? `${prefix}.${key}` : key));
}

describe('i18n', () => {
  it('has the same keys in every dictionary', () => {
    // The Messages type already enforces this at compile time; this guards
    // against a cast or `any` slipping past it.
    expect(leafKeys(tr).sort()).toEqual(leafKeys(en).sort());
  });

  it('has no empty translation', () => {
    expect(leafKeys(tr).filter(key => translate('tr', key as never).trim() === '')).toEqual([]);
  });

  it('translates into the requested locale', () => {
    expect(translate('en', 'header.logOut')).toBe('Log Out');
    expect(translate('tr', 'header.logOut')).toBe('Çıkış yap');
  });

  it('fills named placeholders', () => {
    expect(translate('en', 'header.quota.remaining', { percent: 42 })).toBe('AI tokens: 42% left.');
    expect(translate('tr', 'header.quota.remaining', { percent: 42 })).toBe('AI jetonu: %42 kaldı.');
  });

  it('leaves an unknown placeholder visible instead of printing "undefined"', () => {
    expect(translate('en', 'header.quota.remaining', {})).toBe('AI tokens: {percent}% left.');
  });

  it('falls back to the key itself for a key that does not exist', () => {
    expect(translate('tr', 'does.not.exist' as never)).toBe('does.not.exist');
  });
});
