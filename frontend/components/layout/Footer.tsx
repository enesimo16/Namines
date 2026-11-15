'use client';

import Link from 'next/link';
import Logo from './Logo';
import Footer3DModel from './Footer3DModel';
import { useLocale } from '../../lib/i18n';

export default function Footer() {
  const locale = useLocale();
  const isTr = locale === 'tr';

  return (
    <footer className="relative z-10 w-full bg-surface-900 pt-14 pb-12 text-content-secondary">
      {/* Soft gradient divider */}
      <div
        aria-hidden="true"
        className="w-full h-px bg-gradient-to-r from-transparent via-surface-500/60 to-transparent mb-14"
      />

      <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8">
        {/* Main Directory Grid (Brand + 4 Link Columns + 3D Database Strata Model) */}
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-7 gap-6 sm:gap-8 mb-14 items-start">
          {/* Brand Column */}
          <div className="col-span-2 sm:col-span-3 md:col-span-4 xl:col-span-2 flex flex-col gap-4">
            <div className="flex items-center">
              <Logo size="sm" showWordmark={true} />
            </div>

            <p className="text-xs text-content-muted leading-relaxed max-w-sm">
              {isTr
                ? 'Deterministik veritabanı mimarisi ve yaşam döngüsü yönetim motoru. Sıfır kayma, sıfır veri kaybı ve çoklu motor şema doğrulaması.'
                : 'The deterministic database architecture and lifecycle management engine. Zero drift, zero data loss, and multi-engine schema verification.'}
            </p>

            {/* Quick Links & Social */}
            <div className="flex items-center gap-4 text-xs font-mono text-content-muted pt-1">
              <a
                href="https://github.com/enesimo16/Namines"
                target="_blank"
                rel="noopener noreferrer"
                className="hover:text-content-primary transition-colors"
              >
                GitHub
              </a>
              <a
                href="https://x.com"
                target="_blank"
                rel="noopener noreferrer"
                className="hover:text-content-primary transition-colors"
              >
                X / Twitter
              </a>
              <a
                href="mailto:security@namines.app"
                className="hover:text-content-primary transition-colors"
              >
                {isTr ? 'İletişim' : 'Contact'}
              </a>
            </div>
          </div>

          {/* Col 2 — Product */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              {isTr ? 'Ürün' : 'Product'}
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/new" className="hover:text-content-primary transition-colors">
                  AI Schema Copilot
                </Link>
              </li>
              <li>
                <Link href="/demo" className="hover:text-content-primary transition-colors">
                  {isTr ? 'Şablon Kataloğu' : 'Blueprints Catalog'}
                </Link>
              </li>
              <li>
                <Link href="/canvas" className="hover:text-content-primary transition-colors">
                  {isTr ? 'İnteraktif Tuval' : 'Interactive Canvas'}
                </Link>
              </li>
              <li>
                <Link href="/review" className="hover:text-content-primary transition-colors">
                  Change Review UI
                </Link>
              </li>
              <li>
                <Link href="/pricing" className="hover:text-content-primary transition-colors">
                  {isTr ? 'Fiyatlandırma' : 'Pricing & Plans'}
                </Link>
              </li>
              <li>
                <Link href="/solutions" className="hover:text-content-primary transition-colors">
                  {isTr ? 'Çözümler' : 'Solutions'}
                </Link>
              </li>
            </ul>
          </div>

          {/* Col 3 — Engines (All 6 Dialects) */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              {isTr ? 'Motorlar' : 'Engines'}
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/#engines" className="hover:text-content-primary transition-colors">
                  PostgreSQL 17
                </Link>
              </li>
              <li>
                <Link href="/#engines" className="hover:text-content-primary transition-colors">
                  MySQL 8.4
                </Link>
              </li>
              <li>
                <Link href="/#engines" className="hover:text-content-primary transition-colors">
                  SQL Server 2022
                </Link>
              </li>
              <li>
                <Link href="/#engines" className="hover:text-content-primary transition-colors">
                  Oracle Database
                </Link>
              </li>
              <li>
                <Link href="/#engines" className="hover:text-content-primary transition-colors">
                  MariaDB 11
                </Link>
              </li>
              <li>
                <Link href="/#engines" className="hover:text-content-primary transition-colors">
                  SQLite 3
                </Link>
              </li>
              <li>
                <Link href="/compile" className="hover:text-content-primary transition-colors">
                  {isTr ? '19 Kod Hedefi' : '19 Eject Targets'}
                </Link>
              </li>
            </ul>
          </div>

          {/* Col 4 — Platform & Apps */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              {isTr ? 'Uygulamalar' : 'Platform'}
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/apps" className="hover:text-content-primary transition-colors">
                  {isTr ? 'Tüm Uygulamalar' : 'All Apps'}
                </Link>
              </li>
              <li>
                <Link href="/apps/desk" className="hover:text-content-primary transition-colors">
                  Namines Desk
                </Link>
              </li>
              <li>
                <Link href="/apps/vault" className="hover:text-content-primary transition-colors">
                  Namines Vault
                </Link>
              </li>
              <li>
                <Link href="/apps/ground" className="hover:text-content-primary transition-colors">
                  Namines Ground
                </Link>
              </li>
              <li>
                <Link href="/apps/flow" className="hover:text-content-primary transition-colors">
                  Namines Flow
                </Link>
              </li>
              <li>
                <Link href="/hub" className="hover:text-content-primary transition-colors">
                  Namines Hub
                </Link>
              </li>
              <li>
                <Link href="/docs" className="hover:text-content-primary transition-colors">
                  {isTr ? 'Dokümantasyon' : 'Documentation'}
                </Link>
              </li>
            </ul>
          </div>

          {/* Col 5 — Trust & Legal */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              {isTr ? 'Güven & Yasal' : 'Trust & Legal'}
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/security" className="hover:text-content-primary transition-colors font-medium">
                  {isTr ? 'Güvenlik Mimarisi' : 'Security Whitepaper'}
                </Link>
              </li>
              <li>
                <Link href="/security#ssrf" className="hover:text-content-primary transition-colors">
                  Zero SSRF Guard
                </Link>
              </li>
              <li>
                <Link href="/security#gateway" className="hover:text-content-primary transition-colors">
                  Gateway Guardrails
                </Link>
              </li>
              <li>
                <Link href="/privacy" className="hover:text-content-primary transition-colors font-medium">
                  {isTr ? 'Gizlilik Politikası' : 'Privacy Policy'}
                </Link>
              </li>
              <li>
                <Link href="/terms" className="hover:text-content-primary transition-colors font-medium">
                  {isTr ? 'Kullanım Şartları' : 'Terms of Service'}
                </Link>
              </li>
            </ul>
          </div>

          {/* Col 6 — Interactive 3D Database Strata Model (kutusuz, doğrudan footer içinde) */}
          <div className="col-span-2 sm:col-span-3 md:col-span-2 xl:col-span-1 flex items-center justify-center">
            <Footer3DModel />
          </div>
        </div>

        {/* Bottom Horizontal Bar */}
        <div className="pt-8 border-t border-surface-500/60 flex flex-col sm:flex-row items-center justify-between gap-4 text-xs font-mono text-content-subtle">
          <div className="flex items-center gap-2.5 text-center sm:text-left">
            <span className="inline-flex items-center gap-1.5 text-content-muted">
              <span className="w-1.5 h-1.5 rounded-full bg-accent" />
              <span>{isTr ? 'Sistemler Aktif' : 'All Systems Operational'}</span>
            </span>
            <span>&middot;</span>
            <span>&copy; {new Date().getFullYear()} Namines, Yel LLC. {isTr ? 'Tüm hakları saklıdır.' : 'All rights reserved.'}</span>
          </div>
          <div className="flex items-center justify-center sm:justify-end flex-wrap gap-4 text-xs font-mono">
            <Link href="/privacy" className="hover:text-content-primary transition-colors">
              {isTr ? 'Gizlilik' : 'Privacy'}
            </Link>
            <span>&middot;</span>
            <Link href="/terms" className="hover:text-content-primary transition-colors">
              {isTr ? 'Şartlar' : 'Terms'}
            </Link>
            <span>&middot;</span>
            <Link href="/security" className="hover:text-content-primary transition-colors">
              {isTr ? 'Güvenlik' : 'Security'}
            </Link>
          </div>
        </div>
      </div>
    </footer>
  );
}
