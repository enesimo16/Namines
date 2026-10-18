'use client';

import { useEffect } from 'react';
import { setLocale, type Locale } from '../../lib/i18n';

interface LocaleInitializerProps {
  locale: Locale;
}

/**
 * URL tabanlı rotalarda (/tr ve /en) i18n store'unu senkronize eden hafif bileşen.
 */
export default function LocaleInitializer({ locale }: LocaleInitializerProps) {
  useEffect(() => {
    setLocale(locale);
  }, [locale]);

  return null;
}
