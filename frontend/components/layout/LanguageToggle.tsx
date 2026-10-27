'use client';

import { useState } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Globe, Check } from 'lucide-react';
import { setLocale, useLocale, useT, type Locale } from '../../lib/i18n';

export default function LanguageToggle() {
  const router = useRouter();
  const pathname = usePathname();
  const locale = useLocale();
  const t = useT();
  const [open, setOpen] = useState(false);
  const [spinning, setSpinning] = useState(false);

  const handleSelect = (option: Locale) => {
    setSpinning(true);
    setTimeout(() => setSpinning(false), 450);
    setLocale(option);
    setOpen(false);
    if (pathname === '/' || pathname === '/tr' || pathname === '/en') {
      const targetPath = option === 'tr' ? '/tr' : '/en';
      if (pathname !== targetPath && router) {
        router.push(targetPath);
      }
    }
  };

  return (
    <div
      className="relative inline-flex items-center"
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
    >
      <button
        type="button"
        onClick={() => handleSelect(locale === 'en' ? 'tr' : 'en')}
        aria-label={t('language.label')}
        title={locale === 'en' ? 'English (Click to switch to Türkçe)' : 'Türkçe (İngilizceye geçmek için tıklayın)'}
        className="tap-44 relative w-9 h-9 inline-flex items-center justify-center rounded-full text-content-primary/80 hover:text-content-primary hover:bg-surface-800 transition-colors duration-200 active:scale-90 cursor-pointer select-none shrink-0 group"
      >
        <Globe
          className={`w-[18px] h-[18px] transition-transform duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] group-hover:rotate-12 ${
            spinning ? 'rotate-180 scale-110' : 'rotate-0 scale-100'
          }`}
        />
      </button>

      {open && (
        <div className="absolute right-0 top-full pt-1.5 z-dropdown w-36 animate-dropdown-in">
          <div className="rounded-[10px] bg-surface-900 border border-surface-600/80 shadow-xl p-1.5 flex flex-col gap-0.5">
            <button
              type="button"
              onClick={() => handleSelect('en')}
              className={`w-full px-3 py-2 rounded-[7px] text-xs font-medium flex items-center justify-between transition-colors cursor-pointer ${
                locale === 'en'
                  ? 'bg-surface-800 text-content-primary font-semibold'
                  : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/60'
              }`}
            >
              <span>English</span>
              {locale === 'en' && <Check className="w-3.5 h-3.5 text-content-primary" />}
            </button>
            <button
              type="button"
              onClick={() => handleSelect('tr')}
              className={`w-full px-3 py-2 rounded-[7px] text-xs font-medium flex items-center justify-between transition-colors cursor-pointer ${
                locale === 'tr'
                  ? 'bg-surface-800 text-content-primary font-semibold'
                  : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/60'
              }`}
            >
              <span>Türkçe</span>
              {locale === 'tr' && <Check className="w-3.5 h-3.5 text-content-primary" />}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
