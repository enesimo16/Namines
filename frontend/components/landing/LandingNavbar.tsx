'use client';

import { useState, useEffect, useRef, type ReactNode } from 'react';
import Link from 'next/link';
import { useRouter, usePathname } from 'next/navigation';
import {
  Bell,
  ChevronDown,
  ChevronRight,
  Menu,
  X,
  Layers,
  GitPullRequest,
  FileCode2,
  Compass,
  Database,
  Lock,
  Activity,
  Zap,
  ArrowUpRight,
} from 'lucide-react';
import Logo from '../layout/Logo';
import AppBrandMark from '../layout/AppBrandMark';
import ThemeToggleButton from './ThemeToggleButton';
import LanguageToggle from '../layout/LanguageToggle';
import TalkToSalesModal from './TalkToSalesModal';
import { useAuthStore } from '../../store/useAuthStore';
import { useIsClient } from '../../hooks/useIsClient';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useT } from '../../lib/i18n';

interface DropdownCardItem {
  title: string;
  description: string;
  href: string;
  icon: ReactNode;
  badge?: string;
}

interface MenuLinkItem {
  label: string;
  href: string;
  badge?: string;
  external?: boolean;
  action?: 'sales';
}

interface MenuColumn {
  heading: string;
  items: MenuLinkItem[];
}

const PRODUCTS_ITEMS: DropdownCardItem[] = [
  {
    title: 'Schema Architect',
    description: 'Design deterministic tables and relations from prompt, code, or live DB',
    href: '/new',
    icon: <Layers className="w-4 h-4 text-accent-text" />,
  },
  {
    title: 'PR Change Review',
    description: 'Detect migration risks, breaking diffs, and affected application code',
    href: '/review',
    icon: <GitPullRequest className="w-4 h-4 text-accent-text" />,
  },
  {
    title: 'Eject & Compile',
    description: 'Generate verified DDL across 6 SQL dialects and 19 target stacks',
    href: '/compile',
    icon: <FileCode2 className="w-4 h-4 text-accent-text" />,
  },
  {
    title: 'Blueprints Catalog',
    description: 'Explore 65+ production schemas with interactive graph and linter',
    href: '/demo',
    icon: <Compass className="w-4 h-4 text-accent-text" />,
  },
];

const SOLUTIONS_COLUMNS: MenuColumn[] = [
  {
    heading: 'Use cases',
    items: [
      { label: 'Zero-Drift CI/CD', href: '/solutions#ci-cd' },
      { label: 'Legacy DB Modernization', href: '/solutions#modernize' },
      { label: 'AI Agent Data Layer', href: '/solutions#ai-layer' },
      { label: 'Multi-Engine Migration', href: '/solutions#multi-engine' },
    ],
  },
  {
    heading: 'Teams & trust',
    items: [
      { label: 'Backend & Platform Teams', href: '/solutions#teams' },
      { label: 'Team Governance & Audit', href: '/solutions#governance' },
      { label: 'Enterprise Security & SSRF', href: '/security' },
      { label: 'Schema Hub & Forking', href: '/hub' },
    ],
  },
];

const DEVELOPERS_COLUMNS: MenuColumn[] = [
  {
    heading: 'Documentation',
    items: [
      { label: 'Quickstart Guide', href: '/docs#quickstart' },
      { label: 'CLI & Universal MCP Server', href: '/docs#cli-mcp', badge: 'MCP' },
      { label: 'Gateway REST API', href: '/docs#gateway' },
      { label: 'Canonical IR Reference', href: '/docs#ir' },
      { label: 'SQL Dialect Matrix', href: '/docs#dialects' },
    ],
  },
  {
    heading: 'Ecosystem',
    items: [
      { label: 'GitHub PR Bot & CI', href: '/docs#github-bot' },
      { label: 'Docker Sandbox Runner', href: '/docs#sandbox' },
      { label: '19 Eject Targets', href: '/compile' },
      { label: 'Schema Hub', href: '/hub' },
    ],
  },
];

const APPS_ITEMS: DropdownCardItem[] = [
  {
    title: 'Namines Desk',
    description: 'Browser-based SQL workspace and live data studio',
    href: '/apps/desk',
    icon: <AppBrandMark slug="desk" className="w-5 h-5 text-content-primary" />,
  },
  {
    title: 'Namines Vault',
    description: 'Encrypted backups, snapshots, and connection keys',
    href: '/apps/vault',
    icon: <AppBrandMark slug="vault" className="w-5 h-5 text-content-primary" />,
  },
  {
    title: 'Namines Ground',
    description: 'Managed Postgres provisioning and query telemetry',
    href: '/apps/ground',
    icon: <AppBrandMark slug="ground" className="w-5 h-5 text-content-primary" />,
  },
  {
    title: 'Namines Flow',
    description: 'Event-driven schema automation and webhook rules',
    href: '/apps/flow',
    icon: <AppBrandMark slug="flow" className="w-5 h-5 text-content-primary" />,
    badge: 'New',
  },
];

const RESOURCES_COLUMNS: MenuColumn[] = [
  {
    heading: 'Learn',
    items: [
      { label: 'Documentation', href: '/docs' },
      { label: 'Blueprints Gallery', href: '/demo' },
      { label: 'Schema Hub', href: '/hub' },
      { label: 'Security Architecture', href: '/security' },
    ],
  },
  {
    heading: 'Support & legal',
    items: [
      { label: 'GitHub Repository', href: 'https://github.com/enesimo16/Namines', external: true },
      { label: 'Issues & Support', href: 'https://github.com/enesimo16/Namines/issues', external: true },
      { label: 'Terms of Service', href: '/terms' },
      { label: 'Privacy Policy', href: '/privacy' },
    ],
  },
];

type OpenMenuId = 'products' | 'solutions' | 'developers' | 'apps' | 'resources' | 'notifications' | null;
type MobileSectionId = 'products' | 'solutions' | 'developers' | 'apps' | 'resources' | null;

export default function LandingNavbar() {
  const t = useT();
  const router = useRouter();
  const pathname = usePathname();
  // Oturum bayrağı localStorage'dan gelir; sunucu HTML'iyle eşleşmesi için hidrasyondan önce false.
  const isAuthenticatedStored = useAuthStore((s) => s.isAuthenticated);
  const isAuthenticated = useIsClient() && isAuthenticatedStored;
  const openAuthModal = useAuthModalStore((s) => s.open);

  const [openMenu, setOpenMenu] = useState<OpenMenuId>(null);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [mobileAccordion, setMobileAccordion] = useState<MobileSectionId>('products');
  const [isSalesModalOpen, setIsSalesModalOpen] = useState(false);
  const [hasScrolled, setHasScrolled] = useState(false);

  const navRef = useRef<HTMLDivElement>(null);
  const closeTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const onScroll = () => {
      setHasScrolled(window.scrollY > 8);
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

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

  const handleNavigate = (href: string, action?: 'sales') => {
    setOpenMenu(null);
    setIsMobileMenuOpen(false);
    if (action === 'sales') {
      setIsSalesModalOpen(true);
      return;
    }
    if (href.startsWith('#')) {
      const targetId = href.slice(1);
      const el = document.getElementById(targetId);
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      } else {
        router.push(`/${href}`);
      }
      return;
    }
    if (href.startsWith('http')) {
      window.open(href, '_blank', 'noopener,noreferrer');
      return;
    }
    router.push(href);
  };

  const isRouteActive = (prefixes: string[]) =>
    prefixes.some((p) => pathname === p || pathname.startsWith(`${p}/`));

  const navButtonClass = (active: boolean, minWidthClass = '') =>
    `h-full px-2.5 inline-flex items-center justify-center gap-1.5 whitespace-nowrap text-[14px] font-medium transition-colors duration-200 cursor-pointer border-b-2 ${minWidthClass} ${
      active
        ? 'border-accent text-content-primary font-semibold'
        : 'border-transparent text-content-secondary hover:text-content-primary'
    }`;

  const renderCardDropdown = (
    menuId: 'products' | 'apps',
    items: DropdownCardItem[],
    footerLabel: string,
    footerHref: string
  ) => {
    const isOpen = openMenu === menuId;
    return (
      <div
        aria-hidden={!isOpen}
        className={`absolute left-0 top-full pt-2 z-dropdown w-[360px] origin-top-left transition-all duration-200 ease-[cubic-bezier(0.16,1,0.3,1)] ${
          isOpen
            ? 'opacity-100 translate-y-0 scale-100 pointer-events-auto visible'
            : 'opacity-0 -translate-y-2 scale-[0.98] pointer-events-none invisible'
        }`}
      >
        <div className="rounded-[var(--radius-card)] bg-surface-900 border border-surface-500 shadow-2xl overflow-hidden">
          <div className="p-2.5 space-y-1">
            {items.map((item) => (
              <button
                key={item.title}
                type="button"
                tabIndex={isOpen ? 0 : -1}
                onClick={() => handleNavigate(item.href)}
                className="w-full text-left flex items-start gap-3 px-3 py-2.5 rounded-[var(--radius-control)] hover:bg-surface-800/90 transition-colors cursor-pointer group"
              >
                <span className="mt-0.5 w-8 h-8 rounded-[var(--radius-control)] bg-surface-800 border border-surface-500/80 inline-flex items-center justify-center shrink-0 group-hover:border-accent/50 transition-colors">
                  {item.icon}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-2">
                    <span className="text-[13px] font-semibold text-content-primary group-hover:text-accent-text transition-colors">
                      {item.title}
                    </span>
                    {item.badge && (
                      <span className="px-1.5 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold uppercase tracking-wide">
                        {item.badge}
                      </span>
                    )}
                  </span>
                  <span className="block text-[12px] text-content-muted leading-snug mt-0.5">
                    {item.description}
                  </span>
                </span>
              </button>
            ))}
          </div>

          <div className="border-t border-surface-500/70 bg-surface-800/40 px-4 py-2.5">
            <button
              type="button"
              tabIndex={isOpen ? 0 : -1}
              onClick={() => handleNavigate(footerHref)}
              className="w-full flex items-center justify-between text-[12px] font-medium text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
            >
              <span>{footerLabel}</span>
              <ChevronRight className="w-3.5 h-3.5 text-content-muted" />
            </button>
          </div>
        </div>
      </div>
    );
  };

  const renderColumnsDropdown = (
    menuId: 'solutions' | 'developers' | 'resources',
    columns: MenuColumn[],
    footer?: { prompt: string; ctaLabel: string; href: string; action?: 'sales' }
  ) => {
    const isOpen = openMenu === menuId;
    return (
      <div
        aria-hidden={!isOpen}
        className={`absolute left-0 top-full pt-2 z-dropdown w-[480px] origin-top-left transition-all duration-200 ease-[cubic-bezier(0.16,1,0.3,1)] ${
          isOpen
            ? 'opacity-100 translate-y-0 scale-100 pointer-events-auto visible'
            : 'opacity-0 -translate-y-2 scale-[0.98] pointer-events-none invisible'
        }`}
      >
        <div className="rounded-[var(--radius-card)] bg-surface-900 border border-surface-500 shadow-2xl overflow-hidden">
          <div className="p-6 grid grid-cols-2 gap-6">
            {columns.map((col, idx) => (
              <div
                key={col.heading}
                className={idx > 0 ? 'border-l border-surface-500/60 pl-6' : ''}
              >
                <div className="text-[11px] font-mono uppercase tracking-wider text-content-muted mb-3">
                  {col.heading}
                </div>
                <div className="flex flex-col gap-2">
                  {col.items.map((item) => (
                    <button
                      key={item.label}
                      type="button"
                      tabIndex={isOpen ? 0 : -1}
                      onClick={() => handleNavigate(item.href, item.action)}
                      className="text-left py-1 text-[13px] font-medium text-content-primary hover:text-accent-text transition-colors cursor-pointer inline-flex items-center justify-between gap-2 group"
                    >
                      <span className="inline-flex items-center gap-1.5">
                        <span>{item.label}</span>
                        {item.badge && (
                          <span className="px-1.5 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold">
                            {item.badge}
                          </span>
                        )}
                      </span>
                      {item.external && (
                        <ArrowUpRight className="w-3.5 h-3.5 text-content-muted group-hover:text-accent-text shrink-0" />
                      )}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>

          {footer && (
            <div className="border-t border-surface-500/70 bg-surface-800/40 px-6 py-3 flex items-center justify-between gap-4">
              <span className="text-[12px] text-content-muted">{footer.prompt}</span>
              <button
                type="button"
                tabIndex={isOpen ? 0 : -1}
                onClick={() => handleNavigate(footer.href, footer.action)}
                className="inline-flex items-center px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-surface-500 text-[12px] font-semibold text-content-primary transition-colors cursor-pointer shrink-0"
              >
                {footer.ctaLabel}
              </button>
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <>
      <div ref={navRef} className="sticky top-0 z-drawer w-full">
        <header
          className={`w-full bg-surface-900/95 backdrop-blur-md transition-colors duration-200 ${
            hasScrolled
              ? 'border-b border-surface-500/80 shadow-xs'
              : 'border-b border-surface-500/40'
          }`}
        >
        <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between gap-4">
          {/* Left Zone: Brand + 6 Top-Level Nav Items */}
          <div className="flex items-center gap-6 lg:gap-8 h-full">
            <Link
              href="/"
              className="flex items-center shrink-0 focus:outline-none focus-visible:ring-2 focus-visible:ring-accent rounded-[var(--radius-control)]"
              aria-label="Namines Home"
            >
              <Logo size="md" showWordmark={true} />
            </Link>

            <nav aria-label="Primary" className="hidden lg:flex items-center h-full gap-0.5">
              {/* 1. Products */}
              <div
                className="relative h-full flex items-center"
                onMouseEnter={() => handleMouseEnter('products')}
                onMouseLeave={handleMouseLeave}
              >
                <button
                  type="button"
                  onClick={() => setOpenMenu((m) => (m === 'products' ? null : 'products'))}
                  aria-expanded={openMenu === 'products'}
                  className={navButtonClass(
                    openMenu === 'products' || isRouteActive(['/new', '/review', '/compile', '/demo']),
                    'min-w-[96px]'
                  )}
                >
                  <span>{t('landing.nav.products')}</span>
                  <ChevronDown
                    className={`w-3.5 h-3.5 opacity-75 transition-transform duration-200 ${
                      openMenu === 'products' ? 'rotate-180' : ''
                    }`}
                  />
                </button>
                {renderCardDropdown(
                  'products',
                  PRODUCTS_ITEMS,
                  t('landing.nav.explorePlatform'),
                  '/solutions'
                )}
              </div>

              {/* 2. Solutions */}
              <div
                className="relative h-full flex items-center"
                onMouseEnter={() => handleMouseEnter('solutions')}
                onMouseLeave={handleMouseLeave}
              >
                <button
                  type="button"
                  onClick={() => setOpenMenu((m) => (m === 'solutions' ? null : 'solutions'))}
                  aria-expanded={openMenu === 'solutions'}
                  className={navButtonClass(
                    openMenu === 'solutions' || isRouteActive(['/solutions', '/security']),
                    'min-w-[100px]'
                  )}
                >
                  <span>{t('landing.nav.solutions')}</span>
                  <ChevronDown
                    className={`w-3.5 h-3.5 opacity-75 transition-transform duration-200 ${
                      openMenu === 'solutions' ? 'rotate-180' : ''
                    }`}
                  />
                </button>
                {renderColumnsDropdown('solutions', SOLUTIONS_COLUMNS, {
                  prompt: t('landing.nav.needVpc'),
                  ctaLabel: t('landing.nav.talkToSales'),
                  href: '#sales',
                  action: 'sales',
                })}
              </div>

              {/* 3. Developers */}
              <div
                className="relative h-full flex items-center"
                onMouseEnter={() => handleMouseEnter('developers')}
                onMouseLeave={handleMouseLeave}
              >
                <button
                  type="button"
                  onClick={() => setOpenMenu((m) => (m === 'developers' ? null : 'developers'))}
                  aria-expanded={openMenu === 'developers'}
                  className={navButtonClass(
                    openMenu === 'developers' || isRouteActive(['/docs', '/hub']),
                    'min-w-[118px]'
                  )}
                >
                  <span>{t('landing.nav.developers')}</span>
                  <ChevronDown
                    className={`w-3.5 h-3.5 opacity-75 transition-transform duration-200 ${
                      openMenu === 'developers' ? 'rotate-180' : ''
                    }`}
                  />
                </button>
                {renderColumnsDropdown('developers', DEVELOPERS_COLUMNS, {
                  prompt: t('landing.nav.readyMcp'),
                  ctaLabel: t('landing.nav.readDocs'),
                  href: '/docs',
                })}
              </div>

              {/* 4. Pricing (Direct Link) */}
              <Link
                href="/pricing"
                onMouseEnter={() => handleMouseEnter(null)}
                className={navButtonClass(isRouteActive(['/pricing']), 'min-w-[102px]')}
              >
                {t('landing.nav.pricing')}
              </Link>

              {/* 5. Apps (New Badge) */}
              <div
                className="relative h-full flex items-center"
                onMouseEnter={() => handleMouseEnter('apps')}
                onMouseLeave={handleMouseLeave}
              >
                <button
                  type="button"
                  onClick={() => setOpenMenu((m) => (m === 'apps' ? null : 'apps'))}
                  aria-expanded={openMenu === 'apps'}
                  className={navButtonClass(
                    openMenu === 'apps' || isRouteActive(['/apps', '/desk', '/vault', '/ground', '/flow']),
                    'min-w-[164px]'
                  )}
                >
                  <span className="inline-flex items-center gap-1.5">
                    <span>{t('landing.nav.apps')}</span>
                    <span className="px-1.5 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold leading-none">
                      {t('landing.nav.newBadge')}
                    </span>
                  </span>
                  <ChevronDown
                    className={`w-3.5 h-3.5 opacity-75 transition-transform duration-200 ${
                      openMenu === 'apps' ? 'rotate-180' : ''
                    }`}
                  />
                </button>
                {renderCardDropdown(
                  'apps',
                  APPS_ITEMS,
                  t('landing.nav.exploreAllApps'),
                  '/apps'
                )}
              </div>

              {/* 6. Resources */}
              <div
                className="relative h-full flex items-center"
                onMouseEnter={() => handleMouseEnter('resources')}
                onMouseLeave={handleMouseLeave}
              >
                <button
                  type="button"
                  onClick={() => setOpenMenu((m) => (m === 'resources' ? null : 'resources'))}
                  aria-expanded={openMenu === 'resources'}
                  className={navButtonClass(
                    openMenu === 'resources' || isRouteActive(['/terms', '/privacy']),
                    'min-w-[104px]'
                  )}
                >
                  <span>{t('landing.nav.resources')}</span>
                  <ChevronDown
                    className={`w-3.5 h-3.5 opacity-75 transition-transform duration-200 ${
                      openMenu === 'resources' ? 'rotate-180' : ''
                    }`}
                  />
                </button>
                {renderColumnsDropdown('resources', RESOURCES_COLUMNS)}
              </div>
            </nav>
          </div>

          {/* Right Zone: Utilities + Auth + Primary CTA */}
          <div className="flex items-center gap-1.5 sm:gap-2.5 shrink-0">
            {/* Notifications Popover */}
            <div
              className="relative inline-flex items-center"
              onMouseEnter={() => handleMouseEnter('notifications')}
              onMouseLeave={handleMouseLeave}
            >
              <button
                type="button"
                onClick={() => setOpenMenu((m) => (m === 'notifications' ? null : 'notifications'))}
                aria-label="Product updates"
                title="Product updates"
                className="tap-44 relative w-10 h-10 inline-flex items-center justify-center rounded-full text-content-secondary hover:text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
              >
                <Bell className="w-[18px] h-[18px]" />
              </button>

              <div
                aria-hidden={openMenu !== 'notifications'}
                className={`absolute right-0 top-full pt-2 z-dropdown w-80 origin-top-right transition-all duration-200 ease-[cubic-bezier(0.16,1,0.3,1)] ${
                  openMenu === 'notifications'
                    ? 'opacity-100 translate-y-0 scale-100 pointer-events-auto visible'
                    : 'opacity-0 -translate-y-2 scale-[0.98] pointer-events-none invisible'
                }`}
              >
                <div className="rounded-[var(--radius-card)] bg-surface-900 border border-surface-500 shadow-2xl p-5 space-y-3">
                  <div className="flex items-center justify-between">
                    <span className="text-[13px] font-semibold text-content-primary">
                      {t('landing.nav.whatsNew')}
                    </span>
                    <span className="px-2 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[11px] font-mono font-semibold">
                      v2.3
                    </span>
                  </div>
                  <p className="text-[12px] text-content-secondary leading-relaxed">
                    {t('landing.nav.mcpUpdate')}
                  </p>
                  <button
                    type="button"
                    tabIndex={openMenu === 'notifications' ? 0 : -1}
                    onClick={() => handleNavigate('/docs#cli-mcp')}
                    className="inline-flex items-center gap-1.5 text-[12px] font-semibold text-accent-text hover:underline cursor-pointer"
                  >
                    <span>{t('landing.nav.viewMcpDocs')}</span>
                    <ChevronRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            </div>

            {/* Desktop-only utility toggles (moved into mobile drawer on < lg so 390px never crowds) */}
            <div className="hidden sm:flex items-center gap-1.5">
              <ThemeToggleButton />
              <LanguageToggle />
            </div>

            {!isAuthenticated ? (
              <button
                type="button"
                onClick={() => openAuthModal()}
                className="tap-44 hidden lg:inline-flex items-center justify-center min-w-[116px] whitespace-nowrap px-3.5 py-2 rounded-[var(--radius-control)] text-[14px] font-semibold text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
              >
                {t('landing.nav.login')}
              </button>
            ) : (
              <Link
                href="/ground"
                className="tap-44 hidden lg:inline-flex items-center justify-center min-w-[116px] whitespace-nowrap px-3.5 py-2 rounded-[var(--radius-control)] text-[14px] font-semibold text-content-primary hover:bg-surface-800 transition-colors"
              >
                {t('landing.nav.workspace')}
              </Link>
            )}

            <Link
              href="/new"
              className="tap-44 hidden sm:inline-flex items-center justify-center min-w-[144px] whitespace-nowrap px-4 py-2 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors shadow-xs"
            >
              {t('landing.tryFree')}
            </Link>

            {/* Mobile Hamburger Button */}
            <button
              type="button"
              onClick={() => setIsMobileMenuOpen((v) => !v)}
              aria-label={isMobileMenuOpen ? 'Close navigation menu' : 'Open navigation menu'}
              aria-expanded={isMobileMenuOpen}
              className="tap-44 lg:hidden w-10 h-10 inline-flex items-center justify-center rounded-[var(--radius-control)] border border-surface-500/80 text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
            >
              {isMobileMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
            </button>
          </div>
        </div>
        </header>

        {/* Mobile Full-Height Drawer (< lg) placed outside <header> so backdrop-blur does not trap position:fixed */}
        {isMobileMenuOpen && (
          <div className="lg:hidden fixed inset-x-0 top-16 bottom-0 z-drawer bg-surface-900 border-t border-surface-500 flex flex-col justify-between overflow-y-auto">
            <div className="px-4 py-5 space-y-5">
              {/* Top 2 Quick Direct Links (DeepL pattern) */}
              <div className="grid grid-cols-1 gap-2">
                <button
                  type="button"
                  onClick={() => handleNavigate('/new')}
                  className="tap-44 w-full flex items-center justify-between px-4 py-3 rounded-[var(--radius-card)] bg-surface-800/70 border border-surface-500/80 text-[14px] font-semibold text-content-primary cursor-pointer"
                >
                  <span className="inline-flex items-center gap-2.5">
                    <Layers className="w-4 h-4 text-accent-text" />
                    <span>Schema Architect</span>
                  </span>
                  <ChevronRight className="w-4 h-4 text-content-muted" />
                </button>
                <button
                  type="button"
                  onClick={() => handleNavigate('/review')}
                  className="tap-44 w-full flex items-center justify-between px-4 py-3 rounded-[var(--radius-card)] bg-surface-800/70 border border-surface-500/80 text-[14px] font-semibold text-content-primary cursor-pointer"
                >
                  <span className="inline-flex items-center gap-2.5">
                    <GitPullRequest className="w-4 h-4 text-accent-text" />
                    <span>PR Change Review</span>
                  </span>
                  <ChevronRight className="w-4 h-4 text-content-muted" />
                </button>
              </div>

              {/* Accordions */}
              <div className="divide-y divide-surface-500/60 border-y border-surface-500/60">
                {/* Products */}
                <div>
                  <button
                    type="button"
                    onClick={() =>
                      setMobileAccordion((s) => (s === 'products' ? null : 'products'))
                    }
                    className="tap-44 w-full flex items-center justify-between py-3 text-[15px] font-semibold text-content-primary cursor-pointer"
                  >
                    <span>Products</span>
                    <ChevronDown
                      className={`w-4 h-4 text-content-muted transition-transform duration-200 ${
                        mobileAccordion === 'products' ? 'rotate-180' : ''
                      }`}
                    />
                  </button>
                  {mobileAccordion === 'products' && (
                    <div className="pb-3 space-y-1">
                      {PRODUCTS_ITEMS.map((item) => (
                        <button
                          key={item.title}
                          type="button"
                          onClick={() => handleNavigate(item.href)}
                          className="tap-44 w-full text-left px-3 py-2 rounded-[var(--radius-control)] hover:bg-surface-800 flex items-center gap-3 cursor-pointer"
                        >
                          {item.icon}
                          <span>
                            <span className="block text-[13px] font-medium text-content-primary">
                              {item.title}
                            </span>
                            <span className="block text-[11px] text-content-muted">
                              {item.description}
                            </span>
                          </span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                {/* Solutions */}
                <div>
                  <button
                    type="button"
                    onClick={() =>
                      setMobileAccordion((s) => (s === 'solutions' ? null : 'solutions'))
                    }
                    className="tap-44 w-full flex items-center justify-between py-3 text-[15px] font-semibold text-content-primary cursor-pointer"
                  >
                    <span>Solutions</span>
                    <ChevronDown
                      className={`w-4 h-4 text-content-muted transition-transform duration-200 ${
                        mobileAccordion === 'solutions' ? 'rotate-180' : ''
                      }`}
                    />
                  </button>
                  {mobileAccordion === 'solutions' && (
                    <div className="pb-3 pl-2 space-y-3">
                      {SOLUTIONS_COLUMNS.map((col) => (
                        <div key={col.heading} className="space-y-1">
                          <div className="px-2 text-[11px] font-mono uppercase tracking-wider text-content-muted">
                            {col.heading}
                          </div>
                          {col.items.map((item) => (
                            <button
                              key={item.label}
                              type="button"
                              onClick={() => handleNavigate(item.href, item.action)}
                              className="tap-44 w-full text-left px-2 py-1.5 text-[13px] text-content-secondary hover:text-content-primary cursor-pointer block"
                            >
                              {item.label}
                            </button>
                          ))}
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Developers */}
                <div>
                  <button
                    type="button"
                    onClick={() =>
                      setMobileAccordion((s) => (s === 'developers' ? null : 'developers'))
                    }
                    className="tap-44 w-full flex items-center justify-between py-3 text-[15px] font-semibold text-content-primary cursor-pointer"
                  >
                    <span>Developers</span>
                    <ChevronDown
                      className={`w-4 h-4 text-content-muted transition-transform duration-200 ${
                        mobileAccordion === 'developers' ? 'rotate-180' : ''
                      }`}
                    />
                  </button>
                  {mobileAccordion === 'developers' && (
                    <div className="pb-3 pl-2 space-y-3">
                      {DEVELOPERS_COLUMNS.map((col) => (
                        <div key={col.heading} className="space-y-1">
                          <div className="px-2 text-[11px] font-mono uppercase tracking-wider text-content-muted">
                            {col.heading}
                          </div>
                          {col.items.map((item) => (
                            <button
                              key={item.label}
                              type="button"
                              onClick={() => handleNavigate(item.href)}
                              className="tap-44 w-full text-left px-2 py-1.5 text-[13px] text-content-secondary hover:text-content-primary cursor-pointer block"
                            >
                              {item.label}
                            </button>
                          ))}
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Pricing */}
                <button
                  type="button"
                  onClick={() => handleNavigate('/pricing')}
                  className="tap-44 w-full flex items-center justify-between py-3 text-[15px] font-semibold text-content-primary cursor-pointer"
                >
                  <span>{t('landing.nav.pricing')}</span>
                  <ChevronRight className="w-4 h-4 text-content-muted" />
                </button>

                {/* Apps */}
                <div>
                  <button
                    type="button"
                    onClick={() => setMobileAccordion((s) => (s === 'apps' ? null : 'apps'))}
                    className="tap-44 w-full flex items-center justify-between py-3 text-[15px] font-semibold text-content-primary cursor-pointer"
                  >
                    <span className="inline-flex items-center gap-2">
                      <span>{t('landing.nav.apps')}</span>
                      <span className="px-1.5 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold">
                        {t('landing.nav.newBadge')}
                      </span>
                    </span>
                    <ChevronDown
                      className={`w-4 h-4 text-content-muted transition-transform duration-200 ${
                        mobileAccordion === 'apps' ? 'rotate-180' : ''
                      }`}
                    />
                  </button>
                  {mobileAccordion === 'apps' && (
                    <div className="pb-3 space-y-1">
                      {APPS_ITEMS.map((item) => (
                        <button
                          key={item.title}
                          type="button"
                          onClick={() => handleNavigate(item.href)}
                          className="tap-44 w-full text-left px-3 py-2 rounded-[var(--radius-control)] hover:bg-surface-800 flex items-center gap-3 cursor-pointer"
                        >
                          {item.icon}
                          <span>
                            <span className="block text-[13px] font-medium text-content-primary">
                              {item.title}
                            </span>
                            <span className="block text-[11px] text-content-muted">
                              {item.description}
                            </span>
                          </span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                {/* Resources */}
                <div>
                  <button
                    type="button"
                    onClick={() =>
                      setMobileAccordion((s) => (s === 'resources' ? null : 'resources'))
                    }
                    className="tap-44 w-full flex items-center justify-between py-3 text-[15px] font-semibold text-content-primary cursor-pointer"
                  >
                    <span>Resources</span>
                    <ChevronDown
                      className={`w-4 h-4 text-content-muted transition-transform duration-200 ${
                        mobileAccordion === 'resources' ? 'rotate-180' : ''
                      }`}
                    />
                  </button>
                  {mobileAccordion === 'resources' && (
                    <div className="pb-3 pl-2 space-y-3">
                      {RESOURCES_COLUMNS.map((col) => (
                        <div key={col.heading} className="space-y-1">
                          <div className="px-2 text-[11px] font-mono uppercase tracking-wider text-content-muted">
                            {col.heading}
                          </div>
                          {col.items.map((item) => (
                            <button
                              key={item.label}
                              type="button"
                              onClick={() => handleNavigate(item.href)}
                              className="tap-44 w-full text-left px-2 py-1.5 text-[13px] text-content-secondary hover:text-content-primary cursor-pointer block"
                            >
                              {item.label}
                            </button>
                          ))}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              {/* Theme & Language row inside mobile drawer */}
              <div className="flex items-center justify-between px-3 py-2.5 rounded-[var(--radius-card)] bg-surface-800/50 border border-surface-500/60">
                <span className="text-[12px] font-mono text-content-muted">
                  Preferences
                </span>
                <div className="flex items-center gap-2">
                  <ThemeToggleButton />
                  <LanguageToggle />
                </div>
              </div>
            </div>

            {/* Bottom Sticky Auth + CTA Footer */}
            <div className="sticky bottom-0 bg-surface-900 border-t border-surface-500 p-4 space-y-2.5">
              {!isAuthenticated ? (
                <button
                  type="button"
                  onClick={() => {
                    setIsMobileMenuOpen(false);
                    openAuthModal();
                  }}
                  className="tap-44 w-full py-2.5 rounded-[var(--radius-control)] border border-surface-500 text-[14px] font-semibold text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
                >
                  Log in
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => handleNavigate('/ground')}
                  className="tap-44 w-full py-2.5 rounded-[var(--radius-control)] border border-surface-500 text-[14px] font-semibold text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
                >
                  {t('landing.nav.workspace')}
                </button>
              )}
              <button
                type="button"
                onClick={() => handleNavigate('/new')}
                className="tap-44 w-full py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors cursor-pointer"
              >
                {t('landing.tryFree')}
              </button>
            </div>
          </div>
        )}
      </div>

      <TalkToSalesModal
        isOpen={isSalesModalOpen}
        onClose={() => setIsSalesModalOpen(false)}
      />
    </>
  );
}
