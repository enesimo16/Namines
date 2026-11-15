'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import Footer from '../../components/layout/Footer';
import AppBrandMark from '../../components/layout/AppBrandMark';
import { APPS_CATALOG, APP_SLUGS } from '../../lib/appsCatalog';
import { useLocale } from '../../lib/i18n';

export default function AppsIndexPage() {
  const locale = useLocale();
  const isTr = locale === 'tr';

  return (
    <div className="min-h-screen bg-surface-900 text-content-primary flex flex-col">
      <main className="flex-1">
        <section className="border-b border-surface-500/60">
          <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-14 lg:py-20 space-y-4">
            <span className="text-[12px] font-mono uppercase tracking-wider text-accent-text">
              Namines Suite
            </span>
            <h1 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight max-w-3xl leading-[1.12]">
              {isTr
                ? 'Veritabanı yaşam döngünüz için dört odaklı uygulama.'
                : 'Four focused apps for your database lifecycle.'}
            </h1>
            <p className="text-base sm:text-lg text-content-secondary max-w-2xl leading-relaxed">
              {isTr
                ? 'Her uygulamanın ne işe yaradığını inceleyin, ardından tek tıkla çalışma alanına geçin.'
                : 'Review what each application handles, then proceed straight into the workspace you need.'}
            </p>
          </div>
        </section>

        <section className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-14">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6 lg:gap-8">
            {APP_SLUGS.map((slug) => {
              const app = APPS_CATALOG[slug];
              return (
                <article
                  key={slug}
                  className="rounded-[var(--radius-card)] bg-surface-800/35 border border-surface-500/80 p-6 sm:p-8 flex flex-col justify-between gap-6 hover:border-accent/60 transition-colors"
                >
                  <div className="space-y-4">
                    <div className="flex items-center justify-between gap-3">
                      <div className="w-10 h-10 rounded-[var(--radius-control)] bg-surface-900 border border-surface-500 flex items-center justify-center">
                        <AppBrandMark slug={slug} className="w-6 h-6 text-content-primary" />
                      </div>
                      {app.badge && (
                        <span className="px-2 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold">
                          {isTr ? app.badge.tr : app.badge.en}
                        </span>
                      )}
                    </div>
                    <div>
                      <h2 className="text-xl font-bold text-content-primary">{app.name}</h2>
                      <p className="text-[13px] font-medium text-accent-text mt-1">
                        {isTr ? app.tagline.tr : app.tagline.en}
                      </p>
                    </div>
                    <p className="text-[14px] text-content-secondary leading-relaxed">
                      {isTr ? app.description.tr : app.description.en}
                    </p>
                  </div>

                  <div className="pt-4 border-t border-surface-500/60 flex flex-wrap items-center justify-between gap-3">
                    <Link
                      href={`/apps/${slug}`}
                      className="inline-flex items-center gap-1.5 text-[13px] font-semibold text-content-primary hover:text-accent-text transition-colors"
                    >
                      <span>{isTr ? 'Detayları incele' : 'Inspect overview'}</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </Link>
                    <Link
                      href={app.launchHref}
                      className="inline-flex items-center gap-1.5 px-4 py-2 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[13px] font-semibold transition-colors"
                    >
                      <span>{isTr ? app.ctaLabel.tr : app.ctaLabel.en}</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </Link>
                  </div>
                </article>
              );
            })}
          </div>
        </section>
      </main>
      <Footer />
    </div>
  );
}
