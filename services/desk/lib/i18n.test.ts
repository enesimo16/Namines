import { describe, it, expect } from 'vitest';
import { isValidElement, type ReactElement, type ReactNode } from 'react';
import { translate, formatDateTime } from './i18n';
import { rich, stripRich } from './rich';
import en from './messages/en';
import tr from './messages/tr';

function leafKeys(node: unknown, prefix = ''): string[] {
  if (typeof node === 'string') return [prefix];
  return Object.entries(node as Record<string, unknown>)
    .flatMap(([key, value]) => leafKeys(value, prefix ? `${prefix}.${key}` : key));
}

function leafValues(node: unknown): string[] {
  if (typeof node === 'string') return [node];
  return Object.values(node as Record<string, unknown>).flatMap(leafValues);
}

function placeholders(text: string): string[] {
  return (text.match(/\{\w+\}/g) ?? []).sort();
}

describe('Desk i18n', () => {
  it('has the same keys in every dictionary', () => {
    // Messages tipi bunu derlemede zorluyor; bu test bir `as`/`any` kaçağına karşı.
    expect(leafKeys(tr).sort()).toEqual(leafKeys(en).sort());
  });

  it('has no empty translation', () => {
    expect(leafValues(tr).filter(value => value.trim() === '')).toEqual([]);
  });

  it('keeps the same placeholders and markup tags in every translation', () => {
    // Çeviride düşen bir `{count}` ekranda sessizce eksik bilgi, düşen bir
    // `<b>` ise kırık vurgu demek — ikisi de gözle fark edilmesi zor hatalar.
    const tags = (text: string) => (text.match(/<\/?(b|strong|i|code)>/g) ?? []).sort();
    const mismatches = leafKeys(en).filter(key => {
      const source = translate('en', key as never);
      const target = translate('tr', key as never);
      return placeholders(source).join() !== placeholders(target).join()
        || tags(source).join() !== tags(target).join();
    });
    expect(mismatches).toEqual([]);
  });

  it('translates into the requested locale', () => {
    expect(translate('en', 'shell.signOut')).toBe('Sign out');
    expect(translate('tr', 'shell.signOut')).toBe('Çıkış yap');
  });

  it('fills named placeholders', () => {
    expect(translate('en', 'common.tables', { count: 3 })).toBe('3 tables');
    expect(translate('tr', 'common.tables', { count: 3 })).toBe('3 tablo');
  });

  it('falls back to the key itself for a key that does not exist', () => {
    expect(translate('tr', 'does.not.exist' as never)).toBe('does.not.exist');
  });

  it('formats dates in the selected locale', () => {
    const value = Date.UTC(2026, 8, 27, 12, 0, 0);
    expect(formatDateTime('en', value)).toBe(new Date(value).toLocaleString('en-US'));
    expect(formatDateTime('tr', value)).toBe(new Date(value).toLocaleString('tr-TR'));
  });
});

describe('rich', () => {
  function children(node: ReactNode): ReactNode[] {
    const element = node as ReactElement<{ children: ReactNode[] }>;
    return ([] as ReactNode[]).concat(element.props.children);
  }

  it('turns known tags into elements and keeps surrounding text', () => {
    const parts = children(rich('Type <b>DELETE</b> to continue.'));
    expect(parts[0]).toBe('Type ');
    expect(isValidElement(parts[1]) && parts[1].type).toBe('b');
    expect(parts[2]).toBe(' to continue.');
  });

  it('leaves unknown tags as plain text instead of rendering HTML', () => {
    expect(children(rich('<img src=x onerror=alert(1)>'))).toEqual(['<img src=x onerror=alert(1)>']);
  });

  it('strips markup for plain-text comparisons', () => {
    expect(stripRich('At <code>{store}</code>')).toBe('At {store}');
  });
});
