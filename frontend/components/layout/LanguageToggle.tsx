'use client';

import { LOCALES, setLocale, useLocale, useT, type Locale } from '../../lib/i18n';

const SHORT: Record<Locale, string> = { en: 'EN', tr: 'TR' };

/**
 * Arayüz dili seçici. `<html lang>`'e DOKUNMUYOR — çeviri kısmi olduğu için
 * dil, çevrilmiş bileşenlerin kendi `lang` özniteliğiyle beyan ediliyor
 * (bkz. `useLangAttr`, WCAG 3.1.2).
 */
export default function LanguageToggle() {
  const locale = useLocale();
  const t = useT();

  return (
    <div
      role="group"
      aria-label={t('language.label')}
      title={locale === 'tr' ? t('language.betaNote') : undefined}
      className="inline-flex items-center rounded-full border border-content-primary/10 bg-white/[0.04] p-0.5 shrink-0"
    >
      {LOCALES.map(option => {
        const active = option === locale;
        return (
          <button
            key={option}
            type="button"
            onClick={() => setLocale(option)}
            aria-pressed={active}
            aria-label={t(`language.${option}`)}
            className={`tap-44 px-2 py-1 rounded-full text-[10px] font-bold tracking-wider transition-colors cursor-pointer ${
              active ? 'bg-accent text-accent-on' : 'text-content-muted hover:text-content-primary'
            }`}
          >
            {SHORT[option]}
          </button>
        );
      })}
    </div>
  );
}
