'use client';

import { useState, useEffect, useRef } from 'react';
import Link from 'next/link';
import { useRouter, usePathname } from 'next/navigation';
import { Bell } from 'lucide-react';
import Logo from '../layout/Logo';
import ThemeToggleButton from './ThemeToggleButton';
import LanguageToggle from '../layout/LanguageToggle';
import { useAuthStore } from '../../store/useAuthStore';
import { useIsClient } from '../../hooks/useIsClient';
import { useAuthModalStore } from '../../store/useAuthModalStore';

interface DropdownCardItem {
  title: string;
  description: string;
  href: string;
}

const PRODUCTS_ITEMS: DropdownCardItem[] = [
  {
    title: 'Schema Architect',
    description: 'Design deterministic tables and relations from plain text',
    href: '/new',
  },
  {
    title: 'Change Review',
    description: 'Detect migration risks and breaking schema changes',
    href: '/review',
  },
  {
    title: 'Multi-Engine Compiler',
    description: 'Compile verified DDL across six SQL dialects',
    href: '/compile',
  },
  {
    title: 'Blueprints Catalog',
    description: 'Browse 65+ production database architectures by stack and scale',
    href: '/demo',
  },
  {
    title: 'Namines Hub',
    description: 'Explore, fork, and version control community schemas',
    href: '/hub',
  },
];

const SOLUTIONS_ITEMS: DropdownCardItem[] = [
  {
    title: 'Database Governance',
    description: 'Automated pull request checks and schema approvals',
    href: '/review',
  },
  {
    title: 'Rapid Prototyping',
    description: 'Go from plain prompt to verified backend in seconds',
    href: '/new',
  },
  {
    title: 'Multi-Tenant SaaS',
    description: 'Built-in row isolation, UUIDv7, and vector schemas',
    href: '/canvas',
  },
];

const DEVELOPERS_ITEMS: DropdownCardItem[] = [
  {
    title: 'Universal MCP Server',
    description: 'Zero-install npx integration for Cursor, Claude Code, and VS Code',
    href: '/hub',
  },
  {
    title: 'SDK & ORM Eject',
    description: 'Export Prisma, Drizzle, TypeScript, and EF Core',
    href: '/compile',
  },
  {
    title: 'GitHub & Flow Pipelines',
    description: 'Automate schema checks in pull requests and webhooks',
    href: '/flow',
  },
];

const APPS_ITEMS: DropdownCardItem[] = [
  {
    title: 'Namines Desk',
    description: 'Browser-based SQL workspace and live data console',
    href: '/desk',
  },
  {
    title: 'Namines Vault',
    description: 'Zero-knowledge credential and connection storage',
    href: '/vault',
  },
  {
    title: 'Namines Ground',
    description: 'Real-time query telemetry and FinOps monitoring',
    href: '/ground',
  },
  {
    title: 'Namines Flow',
    description: 'Event-driven schema automation pipelines',
    href: '/flow',
  },
];

const RESOURCES_LEARN = [
  { label: 'Documentation', href: 'https://github.com/enesimo16/Namines' },
  { label: 'Universal MCP Guide', href: '/hub' },
  { label: 'Architecture Guides', href: '#spotlights' },
  { label: 'SQL Dialect Reference', href: '#engines' },
  { label: 'Zero-Drift Whitepaper', href: '#zero-drift' },
  { label: 'Security & Compliance', href: '#security' },
];

const RESOURCES_SUPPORT = [
  { label: 'Help Center', href: 'https://github.com/enesimo16/Namines/issues' },
  { label: 'GitHub Repository', href: 'https://github.com/enesimo16/Namines' },
  { label: 'Apps & Integrations', href: '/hub' },
  { label: 'System Status', href: '/ground' },
];

type OpenMenuId = 'products' | 'solutions' | 'developers' | 'apps' | 'resources' | 'notifications' | null;

export default function LandingNavbar() {
  const router = useRouter();
  const pathname = usePathname();
  // Oturum bayrağı localStorage'dan gelir; sunucu HTML'iyle eşleşmesi için hidrasyondan önce false.
  const isAuthenticatedStored = useAuthStore((s) => s.isAuthenticated);
  const isAuthenticated = useIsClient() && isAuthenticatedStored;
  const openAuthModal = useAuthModalStore((s) => s.open);

  const [openMenu, setOpenMenu] = useState<OpenMenuId>(null);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const navRef = useRef<HTMLDivElement>(null);
  const closeTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleMouseEnter = (id: OpenMenuId) => {
    if (closeTimeoutRef.current) {
      clearTimeout(closeTimeoutRef.current);
      closeTimeoutRef.current = null;
    }
    setOpenMenu(id);
  };

  const handleMouseLeave = () => {
    if (closeTimeoutRef.current) clearTimeout(closeTimeoutRef.current);
    closeTimeoutRef.current = setTimeout(() => {
      setOpenMenu(null);
    }, 120);
  };

  useEffect(() => {
    setOpenMenu(null);
    setIsMobileMenuOpen(false);
  }, [pathname]);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (navRef.current && !navRef.current.contains(e.target as Node)) {
        setOpenMenu(null);
      }
    }
    function handleEscape(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setOpenMenu(null);
        setIsMobileMenuOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleEscape);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleEscape);
      if (closeTimeoutRef.current) clearTimeout(closeTimeoutRef.current);
    };
  }, []);

  const handleNavigate = (href: string) => {
    setOpenMenu(null);
    setIsMobileMenuOpen(false);
    if (href.startsWith('#')) {
      const targetId = href.replace('#', '');
      const el = document.getElementById(targetId);
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    } else if (href.startsWith('http')) {
      window.open(href, '_blank', 'noopener,noreferrer');
    } else {
      router.push(href);
    }
  };

  const renderSingleColumnDropdown = (
    menuId: OpenMenuId,
    items: DropdownCardItem[],
    overviewHref?: string
  ) => {
    const isOpen = openMenu === menuId;
    return (
      <div
        aria-hidden={!isOpen}
        className={`absolute left-0 top-full pt-2 z-dropdown w-80 origin-top-left transition-all duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
          isOpen
            ? 'opacity-100 translate-y-0 scale-100 pointer-events-auto visible'
            : 'opacity-0 -translate-y-2 scale-[0.97] pointer-events-none invisible'
        }`}
      >
        <div className="rounded-[12px] bg-surface-900 border border-surface-600/80 shadow-xl p-6">
          <div className="flex flex-col gap-5">
            {items.map((item) => (
              <button
                key={item.title}
                type="button"
                tabIndex={isOpen ? 0 : -1}
                onClick={() => handleNavigate(item.href)}
                className="text-left group cursor-pointer"
              >
                <div className="text-[15px] font-semibold text-content-primary group-hover:text-accent-text transition-colors">
                  {item.title}
                </div>
                <div className="text-[13px] text-content-secondary mt-1 leading-snug">
                  {item.description}
                </div>
              </button>
            ))}
          </div>
          {overviewHref && (
            <>
              <div className="border-t border-surface-600/60 my-4" />
              <button
                type="button"
                tabIndex={isOpen ? 0 : -1}
                onClick={() => handleNavigate(overviewHref)}
                className="text-left text-[15px] font-semibold text-content-primary hover:text-accent-text transition-colors cursor-pointer w-full"
              >
                Overview
              </button>
            </>
          )}
        </div>
      </div>
    );
  };

  return (
    <header
      ref={navRef}
      className="sticky top-0 z-50 w-full bg-surface-900/95 backdrop-blur-md border-b border-surface-600/40 transition-colors"
    >
      <div className="w-full px-5 sm:px-8 lg:px-12 h-[68px] flex items-center justify-between gap-6">
        {/* Left: Logo + Edge-to-Edge Smooth Hover Navigation */}
        <div className="flex items-center gap-8 lg:gap-10 h-full">
          <Link
            href="/"
            onClick={() => setOpenMenu(null)}
            className="tap-44 flex items-center gap-2.5 cursor-pointer shrink-0"
            title="Namines"
          >
            <Logo size="default" showWordmark={true} />
          </Link>

          <nav className="hidden lg:flex items-center gap-1.5 h-full" aria-label="Main Navigation">
            {/* Products */}
            <div
              className="relative h-full flex items-center"
              onMouseEnter={() => handleMouseEnter('products')}
              onMouseLeave={handleMouseLeave}
            >
              <button
                type="button"
                onClick={() => setOpenMenu((m) => (m === 'products' ? null : 'products'))}
                aria-expanded={openMenu === 'products'}
                className={`h-full px-3.5 inline-flex items-center gap-2 text-[15px] font-medium transition-colors duration-200 cursor-pointer border-b-2 ${
                  openMenu === 'products'
                    ? 'border-content-primary text-content-primary font-semibold'
                    : 'border-transparent text-content-primary/85 hover:text-content-primary'
                }`}
              >
                <span>Products</span>
                <span
                  className={`text-xs leading-none opacity-75 transition-transform duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                    openMenu === 'products' ? 'rotate-180' : ''
                  }`}
                >
                  ∨
                </span>
              </button>
              {renderSingleColumnDropdown('products', PRODUCTS_ITEMS, '/new')}
            </div>

            {/* Solutions */}
            <div
              className="relative h-full flex items-center"
              onMouseEnter={() => handleMouseEnter('solutions')}
              onMouseLeave={handleMouseLeave}
            >
              <button
                type="button"
                onClick={() => setOpenMenu((m) => (m === 'solutions' ? null : 'solutions'))}
                aria-expanded={openMenu === 'solutions'}
                className={`h-full px-3.5 inline-flex items-center gap-2 text-[15px] font-medium transition-colors duration-200 cursor-pointer border-b-2 ${
                  openMenu === 'solutions'
                    ? 'border-content-primary text-content-primary font-semibold'
                    : 'border-transparent text-content-primary/85 hover:text-content-primary'
                }`}
              >
                <span>Solutions</span>
                <span
                  className={`text-xs leading-none opacity-75 transition-transform duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                    openMenu === 'solutions' ? 'rotate-180' : ''
                  }`}
                >
                  ∨
                </span>
              </button>
              {renderSingleColumnDropdown('solutions', SOLUTIONS_ITEMS)}
            </div>

            {/* Developers */}
            <div
              className="relative h-full flex items-center"
              onMouseEnter={() => handleMouseEnter('developers')}
              onMouseLeave={handleMouseLeave}
            >
              <button
                type="button"
                onClick={() => setOpenMenu((m) => (m === 'developers' ? null : 'developers'))}
                aria-expanded={openMenu === 'developers'}
                className={`h-full px-3.5 inline-flex items-center gap-2 text-[15px] font-medium transition-colors duration-200 cursor-pointer border-b-2 ${
                  openMenu === 'developers'
                    ? 'border-content-primary text-content-primary font-semibold'
                    : 'border-transparent text-content-primary/85 hover:text-content-primary'
                }`}
              >
                <span>Developers</span>
                <span
                  className={`text-xs leading-none opacity-75 transition-transform duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                    openMenu === 'developers' ? 'rotate-180' : ''
                  }`}
                >
                  ∨
                </span>
              </button>
              {renderSingleColumnDropdown('developers', DEVELOPERS_ITEMS, '/hub')}
            </div>

            {/* Pricing */}
            <button
              type="button"
              onClick={() => handleNavigate('/new')}
              onMouseEnter={() => handleMouseEnter(null)}
              className="h-full px-3.5 inline-flex items-center text-[15px] font-medium text-content-primary/85 hover:text-content-primary transition-colors duration-200 cursor-pointer border-b-2 border-transparent"
            >
              Pricing
            </button>

            {/* Apps [New] */}
            <div
              className="relative h-full flex items-center"
              onMouseEnter={() => handleMouseEnter('apps')}
              onMouseLeave={handleMouseLeave}
            >
              <button
                type="button"
                onClick={() => setOpenMenu((m) => (m === 'apps' ? null : 'apps'))}
                aria-expanded={openMenu === 'apps'}
                className={`h-full px-3.5 inline-flex items-center gap-2 text-[15px] font-medium transition-colors duration-200 cursor-pointer border-b-2 ${
                  openMenu === 'apps'
                    ? 'border-content-primary text-content-primary font-semibold'
                    : 'border-transparent text-content-primary/85 hover:text-content-primary'
                }`}
              >
                <span>Apps</span>
                <span className="px-2 py-0.5 rounded-[6px] bg-success-subtle text-success-text text-xs font-semibold leading-none">
                  New
                </span>
                <span
                  className={`text-xs leading-none opacity-75 transition-transform duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                    openMenu === 'apps' ? 'rotate-180' : ''
                  }`}
                >
                  ∨
                </span>
              </button>
              {renderSingleColumnDropdown('apps', APPS_ITEMS, '/desk')}
            </div>

            {/* Blueprints — Direct Link to /demo (Blueprints Catalog with Sidebar & Hero) */}
            <Link
              href="/demo"
              onMouseEnter={() => handleMouseEnter(null)}
              className="h-full px-3.5 inline-flex items-center text-[15px] font-medium text-content-primary/85 hover:text-content-primary transition-colors duration-200 border-b-2 border-transparent"
            >
              Blueprints
            </Link>

            {/* Resources — 2-column Mega Menu with smooth transition */}
            <div
              className="relative h-full flex items-center"
              onMouseEnter={() => handleMouseEnter('resources')}
              onMouseLeave={handleMouseLeave}
            >
              <button
                type="button"
                onClick={() => setOpenMenu((m) => (m === 'resources' ? null : 'resources'))}
                aria-expanded={openMenu === 'resources'}
                className={`h-full px-3.5 inline-flex items-center gap-2 text-[15px] font-medium transition-colors duration-200 cursor-pointer border-b-2 ${
                  openMenu === 'resources'
                    ? 'border-content-primary text-content-primary font-semibold'
                    : 'border-transparent text-content-primary/85 hover:text-content-primary'
                }`}
              >
                <span>Resources</span>
                <span
                  className={`text-xs leading-none opacity-75 transition-transform duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                    openMenu === 'resources' ? 'rotate-180' : ''
                  }`}
                >
                  ∨
                </span>
              </button>

              <div
                aria-hidden={openMenu !== 'resources'}
                className={`absolute left-0 top-full pt-2 z-dropdown w-[480px] origin-top-left transition-all duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                  openMenu === 'resources'
                    ? 'opacity-100 translate-y-0 scale-100 pointer-events-auto visible'
                    : 'opacity-0 -translate-y-2 scale-[0.97] pointer-events-none invisible'
                }`}
              >
                <div className="rounded-[12px] bg-surface-900 border border-surface-600/80 shadow-xl p-7 grid grid-cols-2 gap-8">
                  <div>
                    <div className="text-xs font-medium text-content-muted mb-4">
                      Learn
                    </div>
                    <div className="flex flex-col gap-3.5">
                      {RESOURCES_LEARN.map((item) => (
                        <button
                          key={item.label}
                          type="button"
                          tabIndex={openMenu === 'resources' ? 0 : -1}
                          onClick={() => handleNavigate(item.href)}
                          className="text-left text-[15px] font-medium text-content-primary hover:text-accent-text transition-colors cursor-pointer"
                        >
                          {item.label}
                        </button>
                      ))}
                    </div>
                  </div>
                  <div className="border-l border-surface-600/60 pl-8">
                    <div className="text-xs font-medium text-content-muted mb-4">
                      Get support
                    </div>
                    <div className="flex flex-col gap-3.5">
                      {RESOURCES_SUPPORT.map((item) => (
                        <button
                          key={item.label}
                          type="button"
                          tabIndex={openMenu === 'resources' ? 0 : -1}
                          onClick={() => handleNavigate(item.href)}
                          className="text-[15px] text-left font-medium text-content-primary hover:text-accent-text transition-colors cursor-pointer"
                        >
                          {item.label}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </nav>
        </div>

        {/* Right: Bell Icon, Theme Icon, Language Icon, Log in, Ücretsiz deneyin */}
        <div className="flex items-center gap-2 sm:gap-3">
          {/* Bell Notification Icon with Smooth Hover Popover */}
          <div
            className="relative inline-flex items-center"
            onMouseEnter={() => handleMouseEnter('notifications')}
            onMouseLeave={handleMouseLeave}
          >
            <button
              type="button"
              onClick={() => setOpenMenu((m) => (m === 'notifications' ? null : 'notifications'))}
              aria-label="Notifications"
              title="Notifications & Updates"
              className="tap-44 relative w-9 h-9 inline-flex items-center justify-center rounded-full text-content-primary/80 hover:text-content-primary hover:bg-surface-800 transition-colors duration-200 active:scale-90 cursor-pointer group"
            >
              <Bell className="w-[18px] h-[18px] transition-transform duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] group-hover:rotate-12" />
            </button>

            <div
              aria-hidden={openMenu !== 'notifications'}
              className={`absolute right-0 top-full pt-2 z-dropdown w-80 origin-top-right transition-all duration-250 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                openMenu === 'notifications'
                  ? 'opacity-100 translate-y-0 scale-100 pointer-events-auto visible'
                  : 'opacity-0 -translate-y-2 scale-[0.97] pointer-events-none invisible'
              }`}
            >
              <div className="rounded-[12px] bg-surface-900 border border-surface-600/80 shadow-xl p-5 space-y-2.5">
                <div className="flex items-center justify-between">
                  <span className="text-[14px] font-semibold text-content-primary">
                    What&apos;s new
                  </span>
                  <span className="px-2 py-0.5 rounded-[6px] bg-surface-800 text-content-secondary text-[11px] font-semibold">
                    v2.3
                  </span>
                </div>
                <div className="text-[13px] text-content-secondary leading-relaxed">
                  Zero-install Universal MCP (`npx -y @namines/mcp`) for Cursor, Claude Code, Codex, Windsurf, and Antigravity is now live.
                </div>
              </div>
            </div>
          </div>

          <ThemeToggleButton />
          <LanguageToggle />

          {!isAuthenticated && (
            <button
              type="button"
              onClick={() => openAuthModal()}
              className="tap-44 hidden sm:inline-flex items-center px-3.5 py-2 rounded-[8px] text-[15px] font-semibold text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
            >
              Log in
            </button>
          )}

          <Link
            href="/new"
            className="tap-44 inline-flex items-center justify-center px-5 py-2.5 rounded-[8px] bg-accent hover:bg-accent-hover text-accent-on text-[15px] font-semibold transition-all duration-200 shadow-xs hover:scale-[1.01] active:scale-95"
          >
            Ücretsiz deneyin
          </Link>

          {/* Mobile Menu Toggle */}
          <button
            type="button"
            onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
            className="tap-44 lg:hidden px-3 py-2 rounded-[8px] text-[15px] font-semibold text-content-primary hover:bg-surface-800 transition-colors"
            aria-label={isMobileMenuOpen ? 'Close menu' : 'Open menu'}
          >
            {isMobileMenuOpen ? 'Close' : 'Menu'}
          </button>
        </div>
      </div>

      {/* Mobile Menu */}
      {isMobileMenuOpen && (
        <div className="lg:hidden border-t border-surface-600/60 bg-surface-900 px-6 py-5 shadow-lg">
          <div className="flex flex-col gap-3.5">
            <div className="text-xs font-semibold text-content-muted uppercase tracking-wider">Products</div>
            {PRODUCTS_ITEMS.map((item) => (
              <button
                key={item.title}
                type="button"
                onClick={() => handleNavigate(item.href)}
                className="text-left text-[15px] font-medium text-content-primary py-1"
              >
                {item.title}
              </button>
            ))}
            <Link
              href="/demo"
              onClick={() => setIsMobileMenuOpen(false)}
              className="text-left text-[15px] font-medium text-content-primary py-1"
            >
              Blueprints
            </Link>
            <div className="border-t border-surface-600/50 pt-4 flex items-center gap-3">
              {!isAuthenticated && (
                <button
                  type="button"
                  onClick={() => {
                    setIsMobileMenuOpen(false);
                    openAuthModal();
                  }}
                  className="flex-1 py-2.5 rounded-[8px] bg-surface-800 text-[15px] font-semibold text-content-primary"
                >
                  Log in
                </button>
              )}
              <Link
                href="/new"
                onClick={() => setIsMobileMenuOpen(false)}
                className="flex-1 text-center py-2.5 rounded-[8px] bg-accent text-accent-on text-[15px] font-semibold"
              >
                Ücretsiz deneyin
              </Link>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}
