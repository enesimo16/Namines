'use client';

import { useState, useEffect } from 'react';
import Link from 'next/link';
import { useRouter, usePathname } from 'next/navigation';
import { ArrowRight, Menu, X, LogIn, ExternalLink } from 'lucide-react';
import Logo from '../layout/Logo';
import ThemeToggleButton from './ThemeToggleButton';
import LanguageToggle from '../layout/LanguageToggle';
import HomeBanner from './HomeBanner';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useT } from '../../lib/i18n';

interface NavItem {
  id: string;
  label: string;
  href: string;
}

export default function LandingNavbar() {
  const router = useRouter();
  const pathname = usePathname();
  const t = useT();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const user = useAuthStore((s) => s.user);
  const openAuthModal = useAuthModalStore((s) => s.open);

  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [activeSection, setActiveSection] = useState('');

  // Close mobile menu on route change
  useEffect(() => {
    setIsMobileMenuOpen(false);
  }, [pathname]);

  // Smooth scroll or navigate for anchor links
  const handleAnchorClick = (e: React.MouseEvent<HTMLAnchorElement>, href: string) => {
    if (href.startsWith('#')) {
      e.preventDefault();
      const targetId = href.replace('#', '');
      const el = document.getElementById(targetId);
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        setActiveSection(targetId);
        setIsMobileMenuOpen(false);
      }
    }
  };

  const handleLogoClick = (e: React.MouseEvent) => {
    e.preventDefault();
    if (pathname === '/' || pathname === '/tr' || pathname === '/en') {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } else {
      router.push('/');
    }
  };

  const navItems: NavItem[] = [
    { id: 'how-it-works', label: 'How it works', href: '#how-it-works' },
    { id: 'engines', label: 'Engines', href: '#engines' },
    { id: 'spotlights', label: 'Architecture', href: '#spotlights' },
    { id: 'security', label: 'Security', href: '#security' },
  ];

  return (
    <>
      {/* 1. Promotional Announcement Banner */}
      <HomeBanner />

      {/* 2. Dedicated Landing Navbar */}
      <header className="sticky top-0 z-50 w-full backdrop-blur-xl bg-surface-900/85 border-b border-surface-500/60 transition-colors">
        <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between gap-4">
          
          {/* Left: Brand Identity */}
          <div className="flex items-center gap-8">
            <Link
              href="/"
              onClick={handleLogoClick}
              className="tap-44 flex items-center gap-2.5 transition-transform hover:scale-[1.01] active:scale-95 cursor-pointer"
              title="Namines — Return to top"
            >
              <Logo size="sm" showWordmark={true} />
            </Link>

            {/* Desktop Navigation Links (Switched to lg:flex for optimal laptop & tablet spacing) */}
            <nav className="hidden lg:flex items-center gap-1">
              {navItems.map((item) => (
                <a
                  key={item.id}
                  href={item.href}
                  onClick={(e) => handleAnchorClick(e, item.href)}
                  className={`px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-medium tracking-wide transition-colors ${
                    activeSection === item.id
                      ? 'text-content-primary bg-surface-800'
                      : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/60'
                  }`}
                >
                  {item.label}
                </a>
              ))}
              <a
                href="https://github.com/enesimo16/Namines"
                target="_blank"
                rel="noopener noreferrer"
                className="px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-medium tracking-wide text-content-secondary hover:text-content-primary hover:bg-surface-800/60 transition-colors inline-flex items-center gap-1"
              >
                <span>Docs</span>
                <ExternalLink className="w-3 h-3 text-content-subtle" />
              </a>
            </nav>
          </div>

          {/* Right: Actions & Theme/Language Controls */}
          <div className="flex items-center gap-1.5 sm:gap-2.5">
            {/* Single Source Theme Toggle */}
            <ThemeToggleButton />

            {/* Language Selector */}
            <LanguageToggle />

            {/* Authentication & Studio Launch */}
            {isAuthenticated ? (
              <Link
                href="/new"
                className="tap-44 hidden md:inline-flex items-center gap-2 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-surface-500/80 text-xs font-bold text-content-primary transition-all shadow-sm"
              >
                <span className="w-2 h-2 rounded-full bg-success" />
                <span className="truncate max-w-[100px] lg:max-w-[130px]">{user?.email?.split('@')[0] || 'Studio'}</span>
              </Link>
            ) : (
              <button
                type="button"
                onClick={() => openAuthModal()}
                className="tap-44 hidden md:inline-flex items-center gap-1.5 px-2.5 sm:px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
              >
                <LogIn className="w-3.5 h-3.5" />
                <span>Sign In</span>
              </button>
            )}

            {/* Launch Studio Primary CTA */}
            <Link
              href="/new"
              className="tap-44 inline-flex items-center justify-center gap-1 sm:gap-1.5 px-3 sm:px-4 py-2 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[11px] sm:text-xs font-bold uppercase tracking-wider transition-all shadow-md shadow-accent/15 cursor-pointer hover:scale-[1.02] active:scale-95"
            >
              <span className="whitespace-nowrap">Launch Studio</span>
              <ArrowRight className="w-3.5 h-3.5 hidden sm:inline" />
            </Link>

            {/* Mobile / Tablet Hamburger Toggle */}
            <button
              type="button"
              onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
              className="tap-44 lg:hidden p-2 rounded-[var(--radius-control)] text-content-secondary hover:text-content-primary hover:bg-surface-800 transition-colors"
              aria-label={isMobileMenuOpen ? 'Close menu' : 'Open menu'}
            >
              {isMobileMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
            </button>
          </div>
        </div>

        {/* Mobile / Tablet Dropdown Menu */}
        {isMobileMenuOpen && (
          <div className="lg:hidden border-t border-surface-500/70 bg-surface-900/98 px-5 py-5 shadow-2xl animate-in slide-in-from-top-2 duration-200">
            <div className="flex flex-col gap-2">
              {navItems.map((item) => (
                <a
                  key={item.id}
                  href={item.href}
                  onClick={(e) => handleAnchorClick(e, item.href)}
                  className="px-3 py-2.5 rounded-[var(--radius-control)] text-sm font-semibold text-content-primary hover:bg-surface-800 transition-colors"
                >
                  {item.label}
                </a>
              ))}
              <a
                href="https://github.com/enesimo16/Namines"
                target="_blank"
                rel="noopener noreferrer"
                className="px-3 py-2.5 rounded-[var(--radius-control)] text-sm font-semibold text-content-secondary hover:text-content-primary hover:bg-surface-800 transition-colors flex items-center justify-between"
              >
                <span>Documentation</span>
                <ExternalLink className="w-4 h-4 text-content-subtle" />
              </a>

              <div className="pt-3 mt-2 border-t border-surface-500/60 flex flex-col gap-2">
                {!isAuthenticated && (
                  <button
                    type="button"
                    onClick={() => {
                      setIsMobileMenuOpen(false);
                      openAuthModal();
                    }}
                    className="w-full flex items-center justify-center gap-2 text-sm font-semibold text-content-primary py-2.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 transition-colors cursor-pointer"
                  >
                    <LogIn className="w-4 h-4" />
                    <span>Sign In</span>
                  </button>
                )}
                <Link
                  href="/new"
                  className="w-full text-center py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-xs font-bold uppercase tracking-wider shadow-sm transition-colors"
                >
                  Launch Studio
                </Link>
              </div>
            </div>
          </div>
        )}
      </header>
    </>
  );
}
