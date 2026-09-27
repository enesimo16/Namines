'use client';

import { useCallback, useSyncExternalStore } from 'react';
import en from './messages/en';
import tr from './messages/tr';

/**
 * Arayüz dili.
 *
 * <b>Neden kütüphane değil:</b> FRONTEND.md'nin kuralı, mevcut paketlerle
 * çözülebilen bir iş için yeni paket eklememek. İhtiyaç iki dil, düz metin ve
 * `{ad}` biçiminde yer tutucu — bir sözlük ve bir hook'a sığıyor.
 *
 * <b>Neden useSyncExternalStore:</b> sayfa sunucuda her zaman İngilizce
 * render ediliyor (sunucu localStorage'ı göremez). Kayıtlı dil ilk render'da
 * okunsaydı istemcinin ilk HTML'i sunucununkinden farklı olur ve React
 * hydration hatası verirdi; bu hook hydration sırasında sunucu değerini
 * kullanıp ardından kayıtlı dile geçiyor.
 */
export type Locale = 'en' | 'tr';

export const LOCALES: readonly Locale[] = ['en', 'tr'];

type Shape<T> = { [K in keyof T]: T[K] extends string ? string : Shape<T[K]> };
type Leaves<T, P extends string = ''> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>
}[keyof T & string];

/** Her dilin sözlüğü bu şekle uymak zorunda — eksik ya da fazla anahtar derlemeyi kırar. */
export type Messages = Shape<typeof en>;
export type MessageKey = Leaves<typeof en>;
export type MessageParams = Record<string, string | number>;

const DICTIONARIES: Record<Locale, Messages> = { en, tr };
const STORAGE_KEY = 'namines-locale';

let current: Locale | null = null;
const listeners = new Set<() => void>();

function readLocale(): Locale {
  if (current) return current;
  try {
    current = localStorage.getItem(STORAGE_KEY) === 'tr' ? 'tr' : 'en';
  } catch {
    current = 'en';
  }
  return current;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

export function setLocale(locale: Locale) {
  current = locale;
  try { localStorage.setItem(STORAGE_KEY, locale); } catch { /* gizli mod */ }
  listeners.forEach(listener => listener());
}

export function useLocale(): Locale {
  return useSyncExternalStore(subscribe, readLocale, () => 'en');
}

/**
 * Çevrilmiş bir bileşenin köküne verilecek `lang` değeri.
 *
 * <b>Neden `<html lang>` değil:</b> ana uygulamanın çevirisi kısmi — yalnızca
 * kabuk (üst şerit, çalışma alanı gezinmesi, ayarlar modalinin iskeleti)
 * Türkçe. `<html lang="tr">` sayfanın geri kalanındaki İngilizce metni de
 * Türkçe sayar: CSS `uppercase` "View blueprints"i "VİEW BLUEPRİNTS" yapıyordu
 * (Türkçede i'nin büyüğü İ) ve ekran okuyucu İngilizceyi Türkçe telaffuzla
 * okuyordu. Dil, yalnızca gerçekten o dilde olan alt ağaçta beyan ediliyor.
 * Desk tamamen çevrildiği için orada `<html lang>` kullanılıyor.
 */
export function useLangAttr(): Locale {
  return useLocale();
}

function lookup(dictionary: Messages, key: string): string | undefined {
  let node: unknown = dictionary;
  for (const part of key.split('.')) {
    if (typeof node !== 'object' || node === null) return undefined;
    node = (node as Record<string, unknown>)[part];
  }
  return typeof node === 'string' ? node : undefined;
}

/** Hook dışında (ör. testlerde) kullanılabilen çeviri. Eksik anahtar İngilizceye, o da yoksa anahtara düşer. */
export function translate(locale: Locale, key: MessageKey, params?: MessageParams): string {
  const message = lookup(DICTIONARIES[locale], key) ?? lookup(en, key) ?? key;
  if (!params) return message;
  return message.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in params ? String(params[name]) : match);
}

export function useT() {
  const locale = useLocale();
  return useCallback(
    (key: MessageKey, params?: MessageParams) => translate(locale, key, params),
    [locale],
  );
}
