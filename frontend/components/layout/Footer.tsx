'use client';

import Link from 'next/link';
import { Moon, Sun, Terminal, ArrowUpRight } from 'lucide-react';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';
import Logo from './Logo';

export default function Footer() {
  const { theme, setTheme } = useHomeThemeStore();

  return (
    <footer className="relative z-10 w-full bg-surface-900 pt-16 pb-14 text-content-secondary">
      {/* Soft gradient divider instead of harsh border */}
      <div
        aria-hidden="true"
        className="w-full h-px bg-gradient-to-r from-transparent via-surface-500/60 to-transparent mb-16"
      />

      <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8">
        {/* Main Grid */}
        <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-8 mb-16">
          {/* Brand Column (Span 2 on large) */}
          <div className="col-span-2 flex flex-col gap-4">
            <div className="flex items-center gap-2.5">
              <Logo />
              <span className="font-mono font-extrabold tracking-widest text-base text-content-primary">
                NAMINES
              </span>
            </div>

            <p className="text-xs text-content-muted leading-relaxed max-w-sm">
              The deterministic database architecture and lifecycle management engine. Zero drift, zero data loss, and multi-engine schema verification.
            </p>

            {/* Quick Links & Social (GitHub, X / Twitter, Contact) */}
            <div className="flex items-center gap-4 text-xs font-mono text-content-muted pt-2">
              <a
                href="https://github.com/enesimo16/Namines"
                target="_blank"
                rel="noopener noreferrer"
                className="hover:text-content-primary transition-colors inline-flex items-center gap-1.5"
              >
                <Terminal className="w-3.5 h-3.5 text-accent-text" />
                <span>GitHub</span>
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
                href="mailto:security@namines.com"
                className="hover:text-content-primary transition-colors"
              >
                Contact
              </a>
            </div>
          </div>

          {/* Col 2 — Product */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              Product
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/new" className="hover:text-content-primary transition-colors">
                  AI Schema Copilot
                </Link>
              </li>
              <li>
                <Link href="/demo" className="hover:text-content-primary transition-colors">
                  Blueprints Catalog
                </Link>
              </li>
              <li>
                <Link href="/canvas" className="hover:text-content-primary transition-colors">
                  Interactive Canvas
                </Link>
              </li>
              <li>
                <Link href="/review" className="hover:text-content-primary transition-colors">
                  Change Review UI
                </Link>
              </li>
              <li>
                <a href="#zero-drift" className="hover:text-content-primary transition-colors">
                  Zero Drift Engine
                </a>
              </li>
              <li>
                <a href="#spotlights" className="hover:text-content-primary transition-colors">
                  Ephemeral Sandbox
                </a>
              </li>
            </ul>
          </div>

          {/* Col 3 — Engines */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              Engines
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <a href="#engines" className="hover:text-content-primary transition-colors">
                  PostgreSQL 17
                </a>
              </li>
              <li>
                <a href="#engines" className="hover:text-content-primary transition-colors">
                  MySQL 8.4
                </a>
              </li>
              <li>
                <a href="#engines" className="hover:text-content-primary transition-colors">
                  SQLite 3
                </a>
              </li>
              <li>
                <a href="#engines" className="hover:text-content-primary transition-colors">
                  SQL Server 2022
                </a>
              </li>
              <li>
                <Link href="/demo" className="hover:text-content-primary transition-colors">
                  Prisma ORM
                </Link>
              </li>
              <li>
                <Link href="/demo" className="hover:text-content-primary transition-colors">
                  EF Core Migrations
                </Link>
              </li>
            </ul>
          </div>

          {/* Col 4 — Governance & Tooling */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              Governance
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/security" className="hover:text-content-primary transition-colors">
                  Security Architecture
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
                <Link href="/security#audit" className="hover:text-content-primary transition-colors">
                  Audit Logging
                </Link>
              </li>
              <li>
                <a
                  href="http://localhost:3200"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 hover:text-content-primary transition-colors"
                >
                  <span>Namines Desk</span>
                  <ArrowUpRight className="w-3 h-3 text-content-subtle" />
                </a>
              </li>
              <li>
                <a href="#builder-grid" className="hover:text-content-primary transition-colors">
                  MCP Server &amp; CLI
                </a>
              </li>
            </ul>
          </div>

          {/* Col 5 — Trust & Legal */}
          <div className="flex flex-col gap-3">
            <span className="font-mono text-xs font-bold uppercase tracking-wider text-content-primary">
              Trust &amp; Legal
            </span>
            <ul className="flex flex-col gap-2 text-xs text-content-muted">
              <li>
                <Link href="/privacy" className="hover:text-content-primary transition-colors font-medium">
                  Privacy Policy
                </Link>
              </li>
              <li>
                <Link href="/terms" className="hover:text-content-primary transition-colors font-medium">
                  Terms of Service
                </Link>
              </li>
              <li>
                <Link href="/security" className="hover:text-content-primary transition-colors font-medium">
                  Security Whitepaper
                </Link>
              </li>
              <li>
                <Link href="/privacy#cookies" className="hover:text-content-primary transition-colors">
                  Cookie Preferences
                </Link>
              </li>
              <li>
                <Link href="/terms#subprocessors" className="hover:text-content-primary transition-colors">
                  Subprocessors Directory
                </Link>
              </li>
              <li>
                <Link href="/security#disclosure" className="hover:text-content-primary transition-colors">
                  Vulnerability Disclosure
                </Link>
              </li>
            </ul>
          </div>
        </div>

        {/* Bottom Horizontal Bar */}
        <div className="pt-8 border-t border-surface-500/60 flex flex-col sm:flex-row items-center justify-between gap-4 text-xs font-mono text-content-subtle">
          {/* Left: Render-style Segmented Theme Toggle */}
          <div className="inline-flex items-center p-0.5 rounded-full bg-surface-800 border border-surface-500/70 text-micro">
            <button
              type="button"
              onClick={() => setTheme('dark')}
              className={`tap-44 flex items-center gap-1 px-2.5 py-1 rounded-full transition-all cursor-pointer ${
                theme === 'dark'
                  ? 'bg-surface-600 text-content-primary font-semibold shadow-sm'
                  : 'text-content-muted hover:text-content-secondary'
              }`}
              title="Force dark theme"
            >
              <Moon className="w-3 h-3 text-accent-text" />
              <span>Dark</span>
            </button>
            <button
              type="button"
              onClick={() => setTheme('light')}
              className={`tap-44 flex items-center gap-1 px-2.5 py-1 rounded-full transition-all cursor-pointer ${
                theme === 'light'
                  ? 'bg-surface-600 text-content-primary font-semibold shadow-sm'
                  : 'text-content-muted hover:text-content-secondary'
              }`}
              title="Force light theme"
            >
              <Sun className="w-3 h-3 text-accent" />
              <span>Light</span>
            </button>
          </div>

          {/* Right: Copyright */}
          <span>&copy; {new Date().getFullYear()} Namines, Inc. All rights reserved.</span>
        </div>
      </div>
    </footer>
  );
}
