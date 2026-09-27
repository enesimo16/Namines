'use client';

import { useCallback, useSyncExternalStore } from 'react';
import en from './messages/en';
import tr from './messages/tr';

/**
 * Desk'in arayüz dili.
 *
 * Ana uygulamadaki `frontend/lib/i18n` ile AYNI tasarım, ama kopya: Desk ayrı
 * bir mikroservis ve `frontend/`'den hiçbir şey import etmiyor (third-phase
 * §5). Kütüphane yok — iki dil, düz metin ve `{ad}` yer tutucu bir sözlükle
 * bir hook'a sığıyor.
 *
 * useSyncExternalStore: sayfa sunucuda İngilizce render ediliyor; kayıtlı dil
 * ilk render'da okunsaydı hydration uyuşmazlığı çıkardı. Hook hydration
 * sırasında sunucu değerini kullanıp ardından kayıtlı dile geçiyor.
 *
 * Sunucudan gelen hata mesajları (Gateway/API) çevrilmiyor — onlar backend'in
 * dilinde (İngilizce) geliyor ve ham hâlleriyle gösterilmeleri bilinçli
 * (bkz. ImportForm: ham mesaj, kullanıcıyı kör bırakmıyor).
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
const STORAGE_KEY = 'namines-desk-locale';
const DATE_LOCALES: Record<Locale, string> = { en: 'en-US', tr: 'tr-TR' };

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

/** Tarih/saat seçili dilin biçiminde — `toLocaleString('en-US')` her yerde sabitti. */
export function formatDateTime(locale: Locale, value: string | number | Date): string {
  return new Date(value).toLocaleString(DATE_LOCALES[locale]);
}

export function dateLocaleOf(locale: Locale): string {
  return DATE_LOCALES[locale];
}

export function useT() {
  const locale = useLocale();
  return useCallback(
    (key: MessageKey, params?: MessageParams) => translate(locale, key, params),
    [locale],
  );
}

/** `t` + tarih biçimleyici birlikte — tarih gösteren ekranlar için. */
export function useI18n() {
  const locale = useLocale();
  const t = useT();
  const dt = useCallback((value: string | number | Date) => formatDateTime(locale, value), [locale]);
  return { t, dt, locale };
}
