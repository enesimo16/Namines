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
      className="relative inline-flex items-center h-8 w-[72px] p-0.5 rounded-full border border-line-strong bg-surface-800/80 shrink-0 select-none shadow-sm overflow-hidden"
    >
      {/* Sliding Active Indicator */}
      <span
        aria-hidden="true"
        className={`absolute top-0.5 bottom-0.5 left-0.5 w-[33px] rounded-full bg-accent shadow-sm transition-transform duration-200 ease-out pointer-events-none ${
          locale === 'tr' ? 'translate-x-[33px]' : 'translate-x-0'
        }`}
      />

      {LOCALES.map(option => {
        const active = option === locale;
        return (
          <button
            key={option}
            type="button"
            onClick={() => setLocale(option)}
            aria-pressed={active}
            aria-label={t(`language.${option}`)}
            className={`tap-44 relative z-10 flex-1 h-7 rounded-full text-[10px] font-mono font-bold tracking-wider flex items-center justify-center transition-colors duration-200 cursor-pointer ${
              active ? 'text-accent-on' : 'text-content-muted hover:text-content-primary'
            }`}
          >
            {SHORT[option]}
          </button>
        );
      })}
    </div>
  );
}
