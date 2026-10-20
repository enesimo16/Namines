'use client';

import React, { useState } from 'react';
import {
  Database, ShieldCheck, Terminal, GitBranch, ArrowRight, ExternalLink,
  Table2, CheckCircle2, Lock, Sparkles, Server, Activity, ArrowUpRight,
} from 'lucide-react';
import LanguageToggle from './LanguageToggle';
import ThemeToggle from './ThemeToggle';
import AuthModal from './AuthModal';
import { useT } from '../lib/i18n';

const NAMINES_FRONTEND = process.env.NAMINES_FRONTEND ?? 'http://localhost:3000';

interface DeskLandingProps {
  onAuthSuccess: (token: string) => void;
}

export default function DeskLanding({ onAuthSuccess }: DeskLandingProps) {
  const t = useT();
  const [authModalOpen, setAuthModalOpen] = useState(false);
  const [authMode, setAuthMode] = useState<'login' | 'signup'>('login');

  const openAuth = (mode: 'login' | 'signup' = 'login') => {
    setAuthMode(mode);
    setAuthModalOpen(true);
  };

  return (
    <div className="landing-wrap">
      {/* ── Üst Gezinme / Header ───────────────────────────────────── */}
      <header className="landing-nav">
        <div className="landing-nav-inner">
          <div className="landing-brand">
            <img src="/brand/namines-symbol.svg" alt="Namines Logo" className="landing-logo-img" />
            <span className="landing-brand-title">Namines Desk</span>
            <span className="brand-badge">beta</span>
          </div>

          <nav className="landing-menu" aria-label="Landing Navigation">
            <a href="#features" className="landing-menu-link">{t('landing.navFeatures')}</a>
            <a href="#security" className="landing-menu-link">{t('landing.navSecurity')}</a>
            <a href="#architecture" className="landing-menu-link">{t('landing.navArchitecture')}</a>
            <a href="#sql" className="landing-menu-link">{t('landing.navConsole')}</a>
          </nav>

          <div className="landing-nav-actions">
            <LanguageToggle />
            <ThemeToggle />
            <a
              href={NAMINES_FRONTEND}
              target="_blank"
              rel="noreferrer"
              className="btn btn-sm btn-ghost"
              title="Namines Studio"
            >
              <span>Namines Studio</span>
              <ExternalLink size={13} />
            </a>
            <button
              type="button"
              className="btn btn-sm btn-primary"
              onClick={() => openAuth('login')}
            >
              <span>{t('authModal.signIn')}</span>
              <ArrowRight size={13} />
            </button>
          </div>
        </div>
      </header>

      {/* ── Hero Bölümü ────────────────────────────────────────────── */}
      <section className="landing-hero">
        <div className="hero-pill">
          <Sparkles size={13} />
          <span>{t('landing.heroBadge')}</span>
        </div>

        <h1 className="hero-title">
          {t('landing.heroTitle')}
        </h1>

        <p className="hero-sub">
          {t('landing.heroSubtitle')}
        </p>

        <div className="hero-actions">
          <button
            type="button"
            className="btn btn-primary hero-btn-main"
            onClick={() => openAuth('signup')}
          >
            <span>{t('landing.enterDesk')}</span>
            <ArrowRight size={15} />
          </button>
          <a
            href={NAMINES_FRONTEND}
            target="_blank"
            rel="noreferrer"
            className="btn hero-btn-sec"
          >
            <span>{t('landing.openStudio')}</span>
            <ArrowUpRight size={15} />
          </a>
        </div>

        {/* ── Canlı Dashboard Önizleme Vitrini (Interactive Mockup) ─── */}
        <div className="landing-preview-wrap">
          <div className="landing-preview-card">
            {/* Mockup Header */}
            <div className="preview-topbar">
              <div className="preview-topbar-left">
                <span className="preview-dot dot-connected" />
                <span className="preview-table-name">{t('landing.previewTable')}</span>
                <span className="badge badge-ok">Live Connected</span>
                <span className="preview-meta mono-cell">12,480 rows · 6 columns</span>
              </div>
              <div className="preview-topbar-right">
                <span className="btn btn-sm">Filter</span>
                <span className="btn btn-sm">Export CSV</span>
                <span className="btn btn-sm btn-primary">+ New Row</span>
              </div>
            </div>

            {/* Mockup Grid */}
            <div className="preview-grid-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th style={{ width: 32 }}>#</th>
                    <th>id</th>
                    <th>customer_name</th>
                    <th>email_address</th>
                    <th>subscription_tier</th>
                    <th>total_spent</th>
                    <th>created_at</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td className="mono-cell text-muted">1</td>
                    <td className="pk-cell">usr_94a2b10e</td>
                    <td>Jane Cooper</td>
                    <td>jane.cooper@enterprise.io</td>
                    <td><span className="badge badge-ok">Enterprise</span></td>
                    <td className="mono-cell">$14,850.00</td>
                    <td className="mono-cell">2026-10-18 11:24</td>
                  </tr>
                  <tr>
                    <td className="mono-cell text-muted">2</td>
                    <td className="pk-cell">usr_33c81f09</td>
                    <td>Wade Warren</td>
                    <td>wade.warren@shopify-corp.com</td>
                    <td><span className="badge badge-ok">Pro Team</span></td>
                    <td className="mono-cell">$3,240.50</td>
                    <td className="mono-cell">2026-10-19 09:12</td>
                  </tr>
                  <tr>
                    <td className="mono-cell text-muted">3</td>
                    <td className="pk-cell">usr_77e029ba</td>
                    <td>Esther Howard</td>
                    <td>esther@developer-tool.dev</td>
                    <td><span className="badge badge-off">Starter</span></td>
                    <td className="mono-cell">$490.00</td>
                    <td className="mono-cell">2026-10-20 08:45</td>
                  </tr>
                </tbody>
              </table>
            </div>

            {/* Mockup Sub-Bar */}
            <div className="preview-footer-strip">
              <div className="preview-strip-left">
                <Activity size={13} style={{ color: 'var(--accent-text)' }} />
                <span>Gateway proxy active: p99 latency 14ms · Zero drift verified against Namines schema</span>
              </div>
              <div className="preview-strip-right">
                <span className="mono-cell">1-3 of 12,480</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ── Neden Namines Desk? 4 Temel Sütun ───────────────────────── */}
      <section id="features" className="landing-section">
        <div className="section-head-center">
          <h2 className="section-title-large">{t('landing.featuresTitle')}</h2>
          <p className="section-sub-large">{t('landing.featuresSubtitle')}</p>
        </div>

        <div className="landing-pillars-grid">
          <div className="pillar-card">
            <div className="pillar-icon-box">
              <Database size={20} />
            </div>
            <h3 className="pillar-card-title">{t('landing.pillar1Title')}</h3>
            <p className="pillar-card-desc">{t('landing.pillar1Desc')}</p>
          </div>

          <div className="pillar-card" id="security">
            <div className="pillar-icon-box">
              <ShieldCheck size={20} />
            </div>
            <h3 className="pillar-card-title">{t('landing.pillar2Title')}</h3>
            <p className="pillar-card-desc">{t('landing.pillar2Desc')}</p>
          </div>

          <div className="pillar-card" id="sql">
            <div className="pillar-icon-box">
              <Terminal size={20} />
            </div>
            <h3 className="pillar-card-title">{t('landing.pillar3Title')}</h3>
            <p className="pillar-card-desc">{t('landing.pillar3Desc')}</p>
          </div>

          <div className="pillar-card" id="architecture">
            <div className="pillar-icon-box">
              <GitBranch size={20} />
            </div>
            <h3 className="pillar-card-title">{t('landing.pillar4Title')}</h3>
            <p className="pillar-card-desc">{t('landing.pillar4Desc')}</p>
          </div>
        </div>
      </section>

      {/* ── Güvenlik & Mimari Detayı ─────────────────────────────────── */}
      <section className="landing-section landing-security-strip">
        <div className="security-box">
          <div className="security-box-header">
            <Lock size={18} style={{ color: 'var(--accent-text)' }} />
            <h3 className="security-title">Enterprise Zero-Trust Security</h3>
          </div>
          <div className="security-features">
            <div className="sec-item">
              <CheckCircle2 size={16} />
              <span>AES-256-GCM Server-side encrypted credential store</span>
            </div>
            <div className="sec-item">
              <CheckCircle2 size={16} />
              <span>SSRF-isolated Gateway database introspection</span>
            </div>
            <div className="sec-item">
              <CheckCircle2 size={16} />
              <span>Write-operation audit logging with API key role validation</span>
            </div>
            <div className="sec-item">
              <CheckCircle2 size={16} />
              <span>Zero client credential persistence (session-scoped tokens only)</span>
            </div>
          </div>
        </div>
      </section>

      {/* ── Call to Action Banner ──────────────────────────────────── */}
      <section className="landing-cta-banner">
        <div className="cta-content">
          <h2>Ready to govern your database with confidence?</h2>
          <p>Sign in with your Namines account or create a new one to connect your live database in minutes.</p>
          <div className="cta-buttons">
            <button
              type="button"
              className="btn btn-primary hero-btn-main"
              onClick={() => openAuth('signup')}
            >
              <span>{t('landing.enterDesk')}</span>
              <ArrowRight size={15} />
            </button>
          </div>
        </div>
      </section>

      {/* ── Footer ─────────────────────────────────────────────────── */}
      <footer className="landing-footer">
        <div className="landing-footer-inner">
          <div className="footer-left">
            <img src="/brand/namines-symbol.svg" alt="Namines" width="20" height="20" />
            <span className="footer-brand-name">Namines Desk</span>
            <span className="footer-copy">© 2026 Namines Inc. {t('landing.footerRights')}</span>
          </div>
          <div className="footer-right">
            <a href={NAMINES_FRONTEND} target="_blank" rel="noreferrer" className="footer-link">Namines Studio</a>
            <a href="mailto:support@namines.com" className="footer-link">Support</a>
          </div>
        </div>
      </footer>

      {/* ── Login / Signup Modal ───────────────────────────────────── */}
      <AuthModal
        isOpen={authModalOpen}
        onClose={() => setAuthModalOpen(false)}
        onSuccess={token => {
          setAuthModalOpen(false);
          onAuthSuccess(token);
        }}
        initialMode={authMode}
      />
    </div>
  );
}
