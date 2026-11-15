'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  ShieldCheck,
} from 'lucide-react';
import Footer from '../../../components/layout/Footer';
import AppBrandMark from '../../../components/layout/AppBrandMark';
import { APPS_CATALOG, APP_SLUGS, type AppSlug } from '../../../lib/appsCatalog';
import { useLocale } from '../../../lib/i18n';

export default function AppOverviewPage() {
  const params = useParams<{ slug: string }>();
  const locale = useLocale();
  const isTr = locale === 'tr';

  const rawSlug = typeof params?.slug === 'string' ? params.slug.toLowerCase() : 'desk';
  const slug: AppSlug = APP_SLUGS.includes(rawSlug as AppSlug) ? (rawSlug as AppSlug) : 'desk';
  const app = APPS_CATALOG[slug];
  const otherApps = APP_SLUGS.filter((s) => s !== slug).map((s) => APPS_CATALOG[s]);

  return (
    <div className="min-h-screen bg-surface-900 text-content-primary flex flex-col">
      <main className="flex-1">
        {/* Hero */}
        <section className="border-b border-surface-500/60">
          <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-12 lg:py-18">
            <Link
              href="/apps"
              className="inline-flex items-center gap-2 text-[13px] font-medium text-content-secondary hover:text-content-primary transition-colors mb-8"
            >
              <ArrowLeft className="w-4 h-4" />
              <span>{isTr ? 'Tüm Namines Uygulamaları' : 'All Namines Apps'}</span>
            </Link>

            <div className="grid grid-cols-1 lg:grid-cols-[1.25fr_0.75fr] gap-10 items-center">
              <div className="space-y-5">
                <div className="flex items-center gap-3">
                  <div className="w-11 h-11 rounded-[var(--radius-card)] bg-surface-800 border border-surface-500 flex items-center justify-center shrink-0">
                    <AppBrandMark slug={app.slug} className="w-6 h-6 text-content-primary" />
                  </div>
                  <div className="flex items-center gap-2.5">
                    <span className="text-[12px] font-mono uppercase tracking-wider text-accent-text">
                      {app.name}
                    </span>
                    {app.badge && (
                      <span className="px-2 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold">
                        {isTr ? app.badge.tr : app.badge.en}
                      </span>
                    )}
                  </div>
                </div>

                <h1 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight leading-[1.12]">
                  {isTr ? app.tagline.tr : app.tagline.en}
                </h1>

                <p className="text-base sm:text-lg text-content-secondary leading-relaxed max-w-2xl">
                  {isTr ? app.description.tr : app.description.en}
                </p>

                <div className="flex flex-wrap items-center gap-3 pt-2">
                  <Link
                    href={app.launchHref}
                    className="tap-44 inline-flex items-center gap-2.5 px-6 py-3 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors shadow-xs"
                  >
                    <span>{isTr ? app.ctaLabel.tr : app.ctaLabel.en}</span>
                    <ArrowRight className="w-4 h-4" />
                  </Link>
                  <Link
                    href={app.secondaryCta.href}
                    className="tap-44 inline-flex items-center px-5 py-3 rounded-[var(--radius-control)] border border-surface-500 bg-surface-800/60 hover:bg-surface-800 text-[14px] font-semibold text-content-primary transition-colors"
                  >
                    {isTr ? app.secondaryCta.labelTr : app.secondaryCta.labelEn}
                  </Link>
                </div>
              </div>

              {/* Right Summary Card */}
              <div className="rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/80 p-6 sm:p-7 space-y-5">
                <div className="flex items-center justify-between border-b border-surface-500/60 pb-4">
                  <span className="text-[12px] font-mono uppercase tracking-wider text-content-muted">
                    {isTr ? 'Hızlı Bakış' : 'Quick Overview'}
                  </span>
                  <span className="text-[12px] font-mono text-accent-text">{app.launchHref}</span>
                </div>

                <ol className="space-y-4">
                  {app.workflow.map((w) => (
                    <li key={w.step} className="flex items-start gap-3.5">
                      <span className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-900 border border-surface-500 text-[11px] font-mono font-semibold text-accent-text flex items-center justify-center shrink-0">
                        {w.step}
                      </span>
                      <div className="space-y-0.5">
                        <div className="text-[14px] font-semibold text-content-primary">
                          {isTr ? w.title.tr : w.title.en}
                        </div>
                        <p className="text-[13px] text-content-secondary leading-relaxed">
                          {isTr ? w.detail.tr : w.detail.en}
                        </p>
                      </div>
                    </li>
                  ))}
                </ol>

                <div className="pt-4 border-t border-surface-500/60 flex items-start gap-2.5 text-[12px] text-content-secondary">
                  <ShieldCheck className="w-4 h-4 text-accent-text shrink-0 mt-0.5" />
                  <span>{isTr ? app.securityNote.tr : app.securityNote.en}</span>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* Capabilities */}
        <section className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-14">
          <h2 className="text-xl sm:text-2xl font-bold mb-6">
            {isTr ? `${app.name} neler sunar?` : `What ${app.name} includes`}
          </h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {app.capabilities.map((cap) => (
              <div
                key={cap.title.en}
                className="rounded-[var(--radius-card)] bg-surface-800/35 border border-surface-500/80 p-6 space-y-3"
              >
                <CheckCircle2 className="w-5 h-5 text-accent-text" />
                <h3 className="text-[16px] font-semibold text-content-primary">
                  {isTr ? cap.title.tr : cap.title.en}
                </h3>
                <p className="text-[13px] text-content-secondary leading-relaxed">
                  {isTr ? cap.detail.tr : cap.detail.en}
                </p>
              </div>
            ))}
          </div>
        </section>

        {/* Explore Other Apps */}
        <section className="border-t border-surface-500/60 bg-surface-800/20">
          <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-12">
            <h2 className="text-lg font-semibold text-content-primary mb-5">
              {isTr ? 'Diğer Namines Uygulamaları' : 'Explore other Namines Apps'}
            </h2>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              {otherApps.map((item) => (
                <Link
                  key={item.slug}
                  href={`/apps/${item.slug}`}
                  className="group rounded-[var(--radius-card)] bg-surface-900/70 border border-surface-500/80 hover:border-accent/60 p-5 flex items-start justify-between gap-4 transition-colors"
                >
                  <div className="space-y-1.5">
                    <div className="flex items-center gap-2">
                      <AppBrandMark slug={item.slug} className="w-4 h-4 text-content-primary" />
                      <span className="text-[14px] font-semibold text-content-primary group-hover:text-accent-text transition-colors">
                        {item.name}
                      </span>
                    </div>
                    <p className="text-[12px] text-content-secondary leading-relaxed">
                      {isTr ? item.tagline.tr : item.tagline.en}
                    </p>
                  </div>
                  <ArrowRight className="w-4 h-4 text-content-muted group-hover:text-accent-text shrink-0 mt-1 transition-colors" />
                </Link>
              ))}
            </div>
          </div>
        </section>
      </main>

      <Footer />
    </div>
  );
}
