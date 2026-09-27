'use client';

import { useEffect } from 'react';
import { LOCALES, setLocale, useLocale, useT, type Locale } from '../lib/i18n';

const SHORT: Record<Locale, string> = { en: 'EN', tr: 'TR' };

/**
 * Arayüz dili seçici — üst şeritte tema anahtarının yanında. `<html lang>`'i
 * de seçili dile çekiyor: ekran okuyucular telaffuzu buna göre seçer
 * (WCAG 3.1.1).
 */
export default function LanguageToggle() {
  const locale = useLocale();
  const t = useT();

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  return (
    <div className="lang-toggle" role="group" aria-label={t('language.label')}>
      {LOCALES.map(option => (
        <button
          key={option}
          type="button"
          className="lang-option"
          onClick={() => setLocale(option)}
          aria-pressed={option === locale}
          aria-label={t(`language.${option}`)}
        >
          {SHORT[option]}
        </button>
      ))}
    </div>
  );
}
