'use client';

import { useState, useRef, useEffect, KeyboardEvent } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import Link from 'next/link';
import { Pencil, Check, FolderOpen, LogOut, X, Settings, Users, ArrowRight, Search, MoreVertical, Compass, ShieldCheck } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore, isUserAdmin } from '../../store/useAuthStore';
import { useTourStore } from '../../store/useTourStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useToastStore } from '../../store/useToastStore';
import { useAIPolicyStore } from '../../store/useAIPolicyStore';
import { useQuotaStore } from '../../store/useQuotaStore';
import AuthModal from '../canvas/panels/AuthModal';
import AIPreferencesModal from '../canvas/panels/AIPreferencesModal';
import TeamModal from './TeamModal';
import QuotaExhaustedModal from '../canvas/panels/QuotaExhaustedModal';
import ProjectSidebar from './ProjectSidebar';
import WorkspaceNav from './WorkspaceNav';
import Logo from './Logo';
import HomeBanner from '../landing/HomeBanner';
import ThemeToggleButton from '../landing/ThemeToggleButton';
import LandingNavbar from '../landing/LandingNavbar';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { activateOnKey } from '../../lib/a11y';
import { useT, useLangAttr, useLocale, type MessageKey } from '../../lib/i18n';
import LanguageToggle from './LanguageToggle';
import GlobalCommandPalette from '../common/GlobalCommandPalette';
import KeyboardShortcutsModal from '../canvas/KeyboardShortcutsModal';
import { useCommandPaletteStore } from '../../store/useCommandPaletteStore';
import { isMac } from '../../lib/shortcuts';
import { useKeyboardShortcut, useSequenceShortcut } from '../../hooks/useKeyboardShortcuts';
import { openDeskHandoff, DESK_URL } from '../../lib/deskHandoff';
import { authService } from '../../services/api';

const HOME_NAV_ITEMS: { id: string; labelKey: MessageKey }[] = [
  { id: 'how-it-works', labelKey: 'header.homeNav.howItWorks' },
  { id: 'engines', labelKey: 'header.homeNav.engines' },
  { id: 'zero-drift', labelKey: 'header.homeNav.zeroDrift' },
  { id: 'spotlights', labelKey: 'header.homeNav.architecture' },
  { id: 'builder-grid', labelKey: 'header.homeNav.ecosystem' },
  { id: 'security', labelKey: 'header.homeNav.security' },
];

export default function Header() {
  const router = useRouter();
  const pathname = usePathname();
  const t = useT();
  const lang = useLangAttr();
  const locale = useLocale();
  const startGuide = useTourStore(s => s.startGuide);
  const { projectName, setProjectName } = useSchemaStore();
  
  const isCanvas = pathname === '/canvas';
  const isCompile = pathname === '/compile';
  // Genel tanıtım sayfası (marketing) — burada proje/workspace bağlamı yok,
  // o yüzden Header buraya özel sadeleştirilmiş bir gezinme gösteriyor
  // (bkz. aşağıdaki `isHome` dallanmaları). Diğer tüm rotalarda Header
  // AYNEN eskisi gibi davranmaya devam ediyor.
  const isHome = pathname === '/';

  const [activeSection, setActiveSection] = useState<string>('');
  const [isEditing, setIsEditing] = useState(false);
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);
  const { isOpen: isAuthModalOpen, open: openAuthModal, close: closeAuthModal } = useAuthModalStore();
  const [isAIPreferencesOpen, setIsAIPreferencesOpen] = useState(false);
  const [isTeamOpen, setIsTeamOpen] = useState(false);
  const [isShortcutsOpen, setIsShortcutsOpen] = useState(false);
  const [isApple, setIsApple] = useState(false);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setIsMobileMenuOpen(false);
  }, [pathname]);

  useEffect(() => {
    setIsApple(isMac());
  }, []);

  // Event listener to open keyboard shortcuts modal
  useEffect(() => {
    const handleOpenShortcuts = () => setIsShortcutsOpen(true);
    window.addEventListener('namines:open-shortcuts-modal', handleOpenShortcuts);
    return () => window.removeEventListener('namines:open-shortcuts-modal', handleOpenShortcuts);
  }, []);

  // Global shortcuts for help modal: ? and Ctrl+/
  useKeyboardShortcut(['?'], () => setIsShortcutsOpen(v => !v), { ignoreInputs: true });
  useKeyboardShortcut(['mod', '/'], () => setIsShortcutsOpen(v => !v), { ignoreInputs: true });

  // Shopify Polaris-style sequence navigation: G then key
  useSequenceShortcut('g', 'd', () => router.push('/canvas'));
  useSequenceShortcut('g', 'c', () => router.push('/compile'));
  useSequenceShortcut('g', 'f', () => router.push('/flow'));
  useSequenceShortcut('g', 'r', () => router.push('/review'));
  useSequenceShortcut('g', 'g', () => router.push('/ground'));
  useSequenceShortcut('g', 'v', () => router.push('/vault'));
  useSequenceShortcut('g', 'k', async () => {
    const activeProjId = useProjectHistoryStore.getState().activeProjectId;
    if (activeProjId) {
      try {
        const { token } = await authService.createDeskHandoffToken();
        openDeskHandoff({ token, projectId: activeProjId, view: 'data' });
        return;
      } catch {
        window.open(DESK_URL, '_blank');
        return;
      }
    }
    window.open(DESK_URL, '_blank');
  });

  // Scroll spy for homepage sections
  useEffect(() => {
    if (!isHome) return;

    const handleScroll = () => {
      const scrollPosition = window.scrollY + 140;
      let current = '';

      for (const item of HOME_NAV_ITEMS) {
        const el = document.getElementById(item.id);
        if (el) {
          const top = el.offsetTop;
          const height = el.offsetHeight;
          if (scrollPosition >= top && scrollPosition < top + height) {
            current = item.id;
            break;
          }
        }
      }

      if (window.innerHeight + window.scrollY >= document.body.offsetHeight - 60) {
        current = HOME_NAV_ITEMS[HOME_NAV_ITEMS.length - 1].id;
      }

      setActiveSection(current);
    };

    window.addEventListener('scroll', handleScroll, { passive: true });
    handleScroll();

    return () => window.removeEventListener('scroll', handleScroll);
  }, [isHome]);

  const handleNavClick = (e: React.MouseEvent<HTMLAnchorElement>, id: string) => {
    e.preventDefault();
    const el = document.getElementById(id);
    if (el) {
      const yOffset = -70;
      const y = el.getBoundingClientRect().top + window.pageYOffset + yOffset;
      window.scrollTo({ top: y, behavior: 'smooth' });
      setActiveSection(id);
      window.history.pushState(null, '', `#${id}`);
    }
  };

  const handleStartGuide = (guideId?: string) => {
    if (guideId) {
      startGuide(guideId, 0);
      return;
    }
    if (pathname === '/canvas') startGuide('canvas', 0);
    else if (pathname === '/compile') startGuide('compile', 0);
    else if (pathname === '/flow') startGuide('flow', 0);
    else if (pathname === '/ground') startGuide('ground', 0);
    else if (pathname === '/vault') startGuide('vault', 0);
    else if (pathname === '/review') startGuide('review', 0);
    else if (pathname === '/new' || pathname === '/') startGuide('new', 0);
    else startGuide('new', 0);
  };

  const { isAuthenticated, user, logout } = useAuthStore();
  const isAdmin = isUserAdmin(user);
  const showToast = useToastStore(state => state.showToast);
  const syncWithCloud = useProjectHistoryStore(s => s.syncWithCloud);
  // Proje geçmişi IndexedDB'den ASENKRON hidrate oluyor. Bu bayrak olmadan
  // aşağıdaki senkron efekti, oturum açık olsa bile HENÜZ YÜKLENMEMİŞ (boş)
  // bir proje listesiyle çalışıp sessizce hiçbir şey göndermiyordu.
  const hasHydrated = useProjectHistoryStore(s => s.hasHydrated);
  const fetchPolicy = useAIPolicyStore(s => s.fetchPolicy);
  const { dailyLimit, remaining, plan } = useQuotaStore();
  const isDevAccount = Boolean(user?.isDev) || plan === 'Dev';
  // Dev hesabi digerlerine gorunmuyor: rozet DEV yaziyor ama bu yalnizca o
  // hesabin kendi ekraninda goruluyor, baska kullaniciya asla gitmiyor.
  const planLabel = isDevAccount ? t('header.plan.dev')
    : plan === 'Team' ? t('header.plan.team')
    : plan === 'Pro' ? t('header.plan.pro')
    : t('header.plan.free');
  const remainingPercent = isDevAccount
    ? 100
    : dailyLimit > 0 ? Math.min(100, Math.max(0, Math.round((remaining / dailyLimit) * 100))) : 100;

  // Sync projects, fetch AI Policy, and fetch quota when authenticated
  useEffect(() => {
    if (!isAuthenticated) return;

    fetchPolicy();
    useQuotaStore.getState().fetchQuota();

    // `hasHydrated` BEKLENİYOR: IndexedDB hidrasyonu asenkron, ve bu efekt
    // sayfa açılışında (oturum localStorage'dan zaten "giriş yapılmış" gelirken)
    // hidrasyondan ÖNCE çalışıyor. Beklenmezse yükleme boş listeyle çalışıp
    // sessizce hiçbir şey göndermiyor ve BİR DAHA denenmiyordu — kullanıcının
    // ana uygulamada gördüğü proje cloud'a hiç çıkmıyor, Namines Desk de
    // (yalnızca cloud'u gördüğü için) "proje yok" diyordu. Gerçek dünyada
    // yaşanan hata tam olarak buydu.
    if (!hasHydrated) return;

    // ÖNCE YÜKLE, SONRA İNDİR. Sıra önemli: `syncWithCloud` yalnızca
    // indiriyor; tek başına çalıştırıldığında yerelde kalmış (ör. misafirken
    // oluşturulmuş, ya da giriş anındaki "senkronla?" diyaloğu kapatılmış)
    // projeler cloud'a HİÇ çıkmıyordu.
    (async () => {
      await useProjectHistoryStore.getState().syncAllToCloud();
      if (syncWithCloud) await syncWithCloud();
    })();
  }, [isAuthenticated, hasHydrated, syncWithCloud, fetchPolicy]);

  // Event listener to open AI settings modal from other components/hooks
  useEffect(() => {
    const handleOpenSettings = () => setIsAIPreferencesOpen(true);
    window.addEventListener('namines:open-ai-settings', handleOpenSettings);
    return () => window.removeEventListener('namines:open-ai-settings', handleOpenSettings);
  }, []);

  const isDefaultProjectName = !projectName || ['New Project', 'Yeni Proje', 'Untitled Project', 'Untitled Schema'].includes(projectName);

  const [draft, setDraft] = useState(isDefaultProjectName ? '' : projectName);

  useEffect(() => {
    setDraft(isDefaultProjectName ? '' : projectName);
  }, [projectName, isDefaultProjectName]);

  const startEditing = () => {
    setDraft(isDefaultProjectName ? '' : projectName);
    setIsEditing(true);
    setTimeout(() => inputRef.current?.select(), 0);
  };

  const commitEdit = () => {
    const trimmed = draft.trim();
    if (trimmed) {
      setProjectName(trimmed);
    } else {
      setProjectName('New Project');
      setDraft('');
    }
    setIsEditing(false);
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') commitEdit();
    if (e.key === 'Escape') {
      setIsEditing(false);
      setDraft(isDefaultProjectName ? '' : projectName);
    }
  };

  /**
   * Marka işareti bir GEZİNME öğesi — veri silen bir eylem değil.
   *
   * Önceden `resetProject()` çağırıyordu: kullanıcı "ana sayfaya döneyim" diye
   * logoya tıkladığında açık olan şema (ölçüldü: 25 tablo) sessizce siliniyordu.
   * Kayıp ancak sonra fark ediliyordu — "Back to Diagram" boş bir tuvale ya da
   * `/new`'e düşüyor ve kullanıcı bunu bir gezinme hatası sanıyordu. Proje
   * sıfırlama hâlâ var, ama ait olduğu yerde: ProjectSidebar'daki "New Project",
   * oturum kapatma ve hesap değiştirme.
   *
   * Zaten ana sayfadaysak gezinmek yerine başa kaydırıyoruz — aynı URL'e
   * push etmek hiçbir şey yapmıyor gibi görünüyordu.
   */
  const handleLogoClick = () => {
    if (isHome) {
      window.scrollTo({ top: 0, behavior: 'smooth' });
      return;
    }
    router.push('/');
  };

  if (pathname?.startsWith('/admin')) {
    return null;
  }

  const isMarketing = pathname === '/' || pathname === '/tr' || pathname === '/en';
  if (isMarketing) {
    return (
      <>
        <LandingNavbar />
        <AuthModal isOpen={isAuthModalOpen} onClose={closeAuthModal} />
      </>
    );
  }

  return (
    <>
      {/* Sticky DEĞİL, normal akışta — bu yüzden aşağı kaydırınca kendiliğinden
          görünümden çıkıyor (JS scroll dinleyicisi gerekmiyor), sticky
          <header> ise altında top:0'a yapışıp kalıyor. Yalnızca tanıtım
          sayfasında (`isHome`): app içi ekranlarda (canvas/compile/...)
          promosyon şeridi göstermek yersiz olurdu. */}
      {isHome && <HomeBanner />}

      <header
        lang={lang}
        className={`flex items-center justify-between h-14 px-2.5 sm:px-6 backdrop-blur-md sticky top-0 z-50 w-full transition-colors border-b ${
          isHome
            ? 'bg-surface-900/90 border-surface-500'
            : 'bg-surface-800/85 border-line'
        }`}
      >
        {/* Left — Logo + Project Info (balanced flex-1) */}
        <div className="flex items-center gap-1.5 sm:gap-3 flex-1 justify-start min-w-0">
          <button
            onClick={handleLogoClick}
            className="shrink-0 flex items-center"
            title={isHome ? t('header.backToTop') : t('header.goToHomepage')}
            aria-label={isHome ? t('header.scrollBackToTop') : t('header.goToNaminesHomepage')}
          >
            <Logo size="sm" />
          </button>

          {isHome ? (
            /* `md` DEĞİL `lg`: yedi bağlantı (6 bölüm + Demo) 768px'de logo ve
               sağdaki hesap/tema grubuyla aynı satıra sığmıyordu — bağlantılar
               kırpılıyor ve tema düğmesiyle üst üste biniyordu. */
            <div className="hidden lg:flex items-center gap-4 xl:gap-6 ml-3 min-w-0">
              {HOME_NAV_ITEMS.map((item) => {
                const isActive = activeSection === item.id;
                return (
                  <a
                    key={item.id}
                    href={`#${item.id}`}
                    onClick={(e) => handleNavClick(e, item.id)}
                    className={`relative py-1 text-xs font-medium transition-colors ${
                      isActive
                        ? 'text-accent-text font-semibold'
                        : 'text-content-muted hover:text-content-primary'
                    }`}
                  >
                    <span>{t(item.labelKey)}</span>
                    {isActive && (
                      <span
                        className="absolute -bottom-2.5 left-0 right-0 h-[2px] rounded-full"
                        style={{ backgroundColor: 'var(--namines-teal-vibrant)' }}
                      />
                    )}
                  </a>
                );
              })}
              <Link
                href="/demo"
                className="text-xs font-medium text-content-muted hover:text-content-primary transition-colors"
              >
                {t('header.demo')}
              </Link>
            </div>
          ) : (
            <>
              <div className="h-4 w-px bg-line-strong shrink-0 hidden sm:block" />

              {/* Workspace / Team */}
              <button
                id="header-workspace-btn"
                onClick={() => setIsSidebarOpen(true)}
                className={`tap-44 hidden md:flex items-center justify-center w-8 h-8 rounded-full border transition-colors shrink-0 ${
                  isSidebarOpen
                    ? 'border-content-primary/40 bg-surface-700 text-content-primary'
                    : 'border-line-strong bg-surface-800/80 text-content-muted hover:text-content-primary hover:border-content-primary/30 hover:bg-surface-700/60'
                }`}
                title={t('header.workspaceTitle')}
                aria-label={t('header.workspace')}
              >
                <FolderOpen className="w-3.5 h-3.5" />
              </button>

              {/* Ekip Butonu */}
              <button
                id="header-team-btn"
                onClick={() => {
                  if (isAuthenticated) setIsTeamOpen(true);
                  else openAuthModal();
                }}
                className={`tap-44 hidden md:flex items-center justify-center w-8 h-8 rounded-full border transition-colors shrink-0 ${
                  isTeamOpen
                    ? 'border-content-primary/40 bg-surface-700 text-content-primary'
                    : 'border-line-strong bg-surface-800/80 text-content-muted hover:text-content-primary hover:border-content-primary/30 hover:bg-surface-700/60'
                }`}
                title={t('header.teamTitle')}
                aria-label={t('header.team')}
              >
                <Users className="w-3.5 h-3.5" />
              </button>

              {/* Project Name Editor — only on canvas / compile / review */}
              {pathname !== '/new' && (
                <>
                  <div className="h-4 w-px bg-line-strong shrink-0 hidden sm:block" />
                  <div className="hidden md:flex items-center">
                    {isEditing ? (
                      <div className="flex items-center gap-1">
                        <input
                          ref={inputRef}
                          aria-label={t('header.projectName')}
                          value={draft}
                          onChange={(e) => setDraft(e.target.value)}
                          onBlur={commitEdit}
                          onKeyDown={handleKeyDown}
                          className="bg-surface-800 border border-line-strong text-content-primary rounded-[var(--radius-control)] px-2.5 py-1 text-xs font-medium w-48 focus:outline-none focus:ring-1 focus:ring-line-strong"
                          maxLength={60}
                          autoFocus
                        />
                        <button
                          onClick={commitEdit}
                          className="text-content-primary hover:text-content-primary-hover p-1 rounded-[var(--radius-control)] hover:bg-surface-700/60 transition-colors cursor-pointer"
                          aria-label={t('header.save')}
                        >
                          <Check className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    ) : (
                      <button
                        onClick={startEditing}
                        className="group flex items-center gap-1.5 text-content-primary hover:text-content-primary-hover px-2.5 py-1 rounded-[var(--radius-control)] hover:bg-surface-700/50 border border-transparent hover:border-line transition-all cursor-pointer"
                        title={t('header.editProjectName')}
                      >
                        <span className="text-xs font-medium max-w-[160px] truncate">{projectName}</span>
                        <Pencil className="w-3 h-3 text-content-muted opacity-0 group-hover:opacity-100 transition-opacity" />
                      </button>
                    )}
                    {/* Global Command Palette Trigger Pill (Shopify Polaris style) */}
                    <button
                      type="button"
                      onClick={() => useCommandPaletteStore.getState().open()}
                      className="tap-44 hidden md:flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-surface-800/80 hover:bg-surface-700/80 border border-line-strong hover:border-content-primary/30 text-content-muted hover:text-content-primary transition-all text-xs cursor-pointer shadow-sm group shrink-0 ml-2"
                      title="Search commands, navigate, or search tables (⌘K / Ctrl+K)"
                      aria-label="Open command palette"
                    >
                      <Search className="w-3.5 h-3.5 text-content-muted group-hover:text-content-primary transition-colors shrink-0" />
                      <span className="text-content-muted group-hover:text-content-secondary hidden xl:inline text-micro">Search...</span>
                      <kbd className="px-1.5 py-0.2 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-[10px] font-mono text-content-muted group-hover:text-content-primary shrink-0">
                        {isApple ? '⌘K' : 'Ctrl+K'}
                      </kbd>
                    </button>
                  </div>
                </>
              )}
            </>
          )}
        </div>

        {/* Middle — Flow/Ground/Vault/Desk switcher or centered Project Title */}
        <div className="flex items-center justify-center shrink-0 min-w-0 px-1 sm:px-2 z-10 relative">
          {pathname === '/new' ? (
            <input
              id="header-new-project-input"
              type="text"
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onBlur={commitEdit}
              onKeyDown={(e) => {
                if (e.key === 'Enter') e.currentTarget.blur();
                if (e.key === 'Escape') {
                  setDraft(isDefaultProjectName ? '' : projectName);
                  e.currentTarget.blur();
                }
              }}
              placeholder={t('header.newProject')}
              aria-label={t('header.projectName')}
              maxLength={60}
              className="bg-transparent text-xs sm:text-sm font-medium text-content-primary placeholder:text-content-muted text-center px-2 sm:px-3 py-1 rounded-[var(--radius-control)] hover:bg-surface-700/40 focus:bg-surface-800 focus:outline-none focus:ring-1 focus:ring-line-strong transition-colors cursor-pointer focus:cursor-text w-28 sm:w-52 truncate"
            />
          ) : (
            <div className="hidden md:flex items-center">
              <WorkspaceNav />
            </div>
          )}
        </div>

        {/* Right — Actions */}
        <div className="flex items-center gap-1 sm:gap-2 flex-1 justify-end min-w-0">
          {/* Mobile 3-Dots / Hamburger Menu Trigger */}
          <button
            type="button"
            onClick={() => setIsMobileMenuOpen(prev => !prev)}
            className="md:hidden tap-44 flex items-center justify-center w-8 h-8 rounded-full border border-line-strong bg-surface-800/80 text-content-primary hover:bg-surface-700 transition-colors shrink-0 cursor-pointer shadow-xs"
            aria-label="Open mobile navigation menu"
            aria-expanded={isMobileMenuOpen}
          >
            {isMobileMenuOpen ? <X className="w-4 h-4" /> : <MoreVertical className="w-4 h-4" />}
          </button>

          {/* Desktop Right Actions Strip */}
          <div className="hidden md:flex items-center gap-1.5 sm:gap-2 min-w-0">
            {isCanvas && <div id="canvas-actions-slot" className="hidden lg:flex items-center gap-1.5" />}
            {/* Unified Rehber / Guide Trigger (Directly starts current page guide) */}
            <button
              type="button"
              onClick={() => handleStartGuide()}
              title={locale === 'tr' ? 'İnteraktif Sayfa Rehberini Başlat' : 'Start Interactive Page Guide'}
              aria-label={locale === 'tr' ? 'İnteraktif Sayfa Rehberi' : 'Interactive Page Guide'}
              className="tap-44 inline-flex items-center gap-1.5 h-7.5 px-3 rounded-full border border-line-strong bg-surface-800/95 hover:bg-surface-700 text-content-secondary hover:text-content-primary text-micro font-semibold transition-all cursor-pointer shadow-xs select-none"
            >
              <Compass className="w-3.5 h-3.5 text-accent shrink-0" />
              <span className="hidden lg:inline">{locale === 'tr' ? 'Rehber' : 'Guide'}</span>
            </button>
            <div id="header-language-toggle">
              <LanguageToggle />
            </div>
            {!isCanvas && (
              <div id="header-theme-toggle">
                <ThemeToggleButton />
              </div>
            )}
            <div id="header-user-account-btn" className="flex items-center">
              {isHome && !isAuthenticated ? (
                <>
                  <Link
                    href="/demo"
                    className="hidden lg:inline text-xs font-medium text-content-muted hover:text-content-primary transition-colors px-2"
                  >
                    {t('header.exploreDemo')}
                  </Link>
                  <button
                    onClick={openAuthModal}
                    className="hidden sm:inline text-xs font-medium text-content-muted hover:text-content-primary transition-colors px-2"
                  >
                    {t('header.signIn')}
                  </button>
                  <Link
                    href="/new"
                    className="inline-flex items-center gap-1.5 py-1.5 px-3.5 rounded-full bg-accent hover:bg-accent-hover text-accent-on text-xs font-bold transition-all cursor-pointer whitespace-nowrap shadow-sm"
                  >
                    {t('header.getStarted')}
                    <ArrowRight className="w-3.5 h-3.5" />
                  </Link>
                </>
              ) : isCanvas || isCompile ? (
                <div className="flex items-center gap-1.5">
                  {isAdmin && (
                    <Link
                      href="/admin"
                      title={locale === 'tr' ? 'Admin Paneli' : 'Admin Console'}
                      aria-label="Admin Console"
                      className="tap-44 w-8 h-8 flex items-center justify-center rounded-full bg-surface-800 hover:bg-surface-700 border border-line-strong text-accent-text hover:text-content-primary transition-all cursor-pointer active:scale-95 shadow-xs"
                    >
                      <ShieldCheck className="w-4 h-4 text-accent-text" />
                    </Link>
                  )}
                  <button
                    onClick={() => setIsAIPreferencesOpen(true)}
                    title={t('header.settings')}
                    aria-label={t('header.openSettings')}
                    className="w-8 h-8 flex items-center justify-center rounded-full bg-surface-800 hover:bg-surface-700 border border-line-strong text-content-primary transition-all cursor-pointer active:scale-95"
                  >
                    <Settings className="w-4 h-4" />
                  </button>
                </div>
              ) : isAuthenticated ? (
                <div className="flex items-center gap-1.5">
                  {isAdmin && (
                    <Link
                      href="/admin"
                      onClick={(e) => e.stopPropagation()}
                      title={locale === 'tr' ? 'Admin Paneli' : 'Admin Console'}
                      aria-label="Admin Console"
                      className="tap-44 flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-surface-800/80 hover:bg-surface-700/80 border border-line-strong text-accent-text hover:text-content-primary text-xs font-semibold transition-colors cursor-pointer shadow-sm shrink-0"
                    >
                      <ShieldCheck className="w-3.5 h-3.5 text-accent-text shrink-0" />
                      <span className="hidden xl:inline text-micro font-medium">{locale === 'tr' ? 'Admin Paneli' : 'Admin Console'}</span>
                    </Link>
                  )}
                  <div
                    onClick={() => setIsAIPreferencesOpen(true)}
                  onKeyDown={activateOnKey(() => setIsAIPreferencesOpen(true))}
                  role="button"
                  tabIndex={0}
                  aria-haspopup="dialog"
                  aria-label={t('header.openAccountSettings')}
                  className="flex items-center gap-2 sm:gap-2.5 pl-1.5 pr-2.5 py-1 bg-surface-800/80 border border-line-strong hover:border-content-primary/30 rounded-full select-none hover:bg-surface-700/80 transition-colors cursor-pointer shadow-sm"
                >
                  {/* Clean Avatar Badge */}
                  <div
                    className="relative w-6 h-6 rounded-full bg-accent text-accent-on flex items-center justify-center text-[10px] font-mono font-bold uppercase tracking-wider select-none shrink-0"
                    title={user?.username}
                  >
                    {user?.username ? user.username.substring(0, 2).toUpperCase() : 'US'}
                  </div>
                  <div className="hidden sm:flex flex-col gap-0.5 justify-center">
                    <span className="text-xs font-semibold text-content-primary leading-none max-w-[95px] truncate tracking-wide" title={user?.username}>
                      {user?.username}
                    </span>
                    <span className="text-[10px] font-mono text-content-muted font-bold leading-none uppercase tracking-widest mt-0.5">
                      {planLabel}
                    </span>
                  </div>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      logout();
                      showToast(t('header.loggedOut'), 'info');
                    }}
                    className="tap-44 p-1 text-content-muted hover:text-content-primary hover:bg-surface-700 rounded-full transition-colors cursor-pointer ml-0.5 shrink-0"
                    title={t('header.logOut')}
                    aria-label={t('header.logOut')}
                  >
                    <LogOut className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
              ) : (
                <button
                  id="auth-modal-trigger"
                  onClick={openAuthModal}
                  className="flex items-center justify-center py-1.5 px-4 rounded-full bg-accent hover:bg-accent-hover text-accent-on text-xs font-bold transition-all cursor-pointer whitespace-nowrap shadow-sm"
                >
                  <span className="hidden sm:inline">{t('header.loginSignUp')}</span>
                  <span className="inline sm:hidden">{t('header.login')}</span>
                </button>
              )}
            </div>
          </div>
        </div>
      </header>

      {/* Mobile Menu Popover / Drawer */}
      {isMobileMenuOpen && (
        <div
          className="fixed inset-0 top-14 z-[90] bg-surface-900/95 backdrop-blur-xl md:hidden flex flex-col p-4 overflow-y-auto animate-in fade-in slide-in-from-top-4 duration-150 border-b border-line shadow-2xl"
          onClick={() => setIsMobileMenuOpen(false)}
        >
          <div className="space-y-3" onClick={e => e.stopPropagation()}>
            {/* Quick Actions (Projects, Team, Search) */}
            <div className="bg-surface-800/90 border border-line-strong rounded-[var(--radius-card)] p-3">
              <div className="flex items-center justify-between gap-2 mb-2 pb-2 border-b border-line">
                <span className="text-xs font-bold text-content-primary truncate">{projectName || 'Project Workspace'}</span>
                {isAuthenticated && (
                  <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded-full bg-surface-700 border border-line text-content-muted">
                    {planLabel}
                  </span>
                )}
              </div>
              <div className="grid grid-cols-2 gap-2">
                <button
                  onClick={() => { setIsSidebarOpen(true); setIsMobileMenuOpen(false); }}
                  className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-700/60 hover:bg-surface-700 text-xs font-medium text-content-primary transition-colors cursor-pointer"
                >
                  <FolderOpen className="w-4 h-4 text-content-muted" />
                  <span>Projects</span>
                </button>
                {isAuthenticated && (
                  <button
                    onClick={() => { setIsTeamOpen(true); setIsMobileMenuOpen(false); }}
                    className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-700/60 hover:bg-surface-700 text-xs font-medium text-content-primary transition-colors cursor-pointer"
                  >
                    <Users className="w-4 h-4 text-content-muted" />
                    <span>Team</span>
                  </button>
                )}
                <button
                  onClick={() => { useCommandPaletteStore.getState().open(); setIsMobileMenuOpen(false); }}
                  className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-700/60 hover:bg-surface-700 text-xs font-medium text-content-primary transition-colors cursor-pointer col-span-2"
                >
                  <Search className="w-4 h-4 text-content-muted" />
                  <span>Search Commands (⌘K / Ctrl+K)</span>
                </button>
              </div>
            </div>

            {/* Navigation Routes */}
            {!isHome && (
              <div className="bg-surface-800/90 border border-line-strong rounded-[var(--radius-card)] p-2 space-y-1">
                <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted px-2 py-1 block">
                  Workspace Views
                </span>
                <button
                  onClick={() => { router.push('/canvas'); setIsMobileMenuOpen(false); }}
                  className={`w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
                    pathname === '/canvas' ? 'bg-surface-700 text-content-primary font-bold' : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/50'
                  }`}
                >
                  <span>Diagram Canvas</span>
                  <span className="text-[10px] font-mono text-content-muted">/canvas</span>
                </button>
                <button
                  onClick={() => { router.push('/flow'); setIsMobileMenuOpen(false); }}
                  className={`w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
                    pathname === '/flow' ? 'bg-surface-700 text-content-primary font-bold' : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/50'
                  }`}
                >
                  <span>Namines Flow</span>
                  <span className="text-[10px] font-mono text-content-muted">/flow</span>
                </button>
                <button
                  onClick={() => { router.push('/compile'); setIsMobileMenuOpen(false); }}
                  className={`w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
                    pathname === '/compile' ? 'bg-surface-700 text-content-primary font-bold' : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/50'
                  }`}
                >
                  <span>Compile &amp; Eject</span>
                  <span className="text-[10px] font-mono text-content-muted">/compile</span>
                </button>
                <button
                  onClick={() => { router.push('/ground'); setIsMobileMenuOpen(false); }}
                  className={`w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
                    pathname === '/ground' ? 'bg-surface-700 text-content-primary font-bold' : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/50'
                  }`}
                >
                  <span>Ground Database</span>
                  <span className="text-[10px] font-mono text-content-muted">/ground</span>
                </button>
                <button
                  onClick={() => { router.push('/vault'); setIsMobileMenuOpen(false); }}
                  className={`w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
                    pathname === '/vault' ? 'bg-surface-700 text-content-primary font-bold' : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/50'
                  }`}
                >
                  <span>Vault Backups</span>
                  <span className="text-[10px] font-mono text-content-muted">/vault</span>
                </button>
                <button
                  onClick={() => { router.push('/review'); setIsMobileMenuOpen(false); }}
                  className={`w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
                    pathname.startsWith('/review') ? 'bg-surface-700 text-content-primary font-bold' : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/50'
                  }`}
                >
                  <span>Change Review</span>
                  <span className="text-[10px] font-mono text-content-muted">/review</span>
                </button>
                {isAdmin && (
                  <button
                    onClick={() => { router.push('/admin'); setIsMobileMenuOpen(false); }}
                    className="w-full flex items-center justify-between p-2 rounded-[var(--radius-control)] bg-accent-subtle border border-line-strong text-xs font-bold text-accent-text hover:text-content-primary transition-colors cursor-pointer"
                  >
                    <div className="flex items-center gap-2">
                      <ShieldCheck className="w-4 h-4 text-accent-text" />
                      <span>{locale === 'tr' ? 'Admin Paneli' : 'Admin Console'}</span>
                    </div>
                    <span className="text-[10px] font-mono text-accent-text">/admin</span>
                  </button>
                )}
              </div>
            )}

            {/* Interactive Guide Trigger (Directly starts current page guide) */}
            <button
              type="button"
              onClick={() => {
                setIsMobileMenuOpen(false);
                handleStartGuide();
              }}
              className="w-full flex items-center justify-between p-2.5 rounded-[var(--radius-control)] bg-surface-800/90 hover:bg-surface-700/80 border border-line-strong text-xs font-semibold text-content-primary transition-colors cursor-pointer shadow-xs"
            >
              <div className="flex items-center gap-2">
                <Compass className="w-4 h-4 text-accent shrink-0" />
                <span>{locale === 'tr' ? 'İnteraktif Sayfa Rehberi' : 'Interactive Page Guide'}</span>
              </div>
              <span className="text-[10px] font-mono text-content-muted uppercase">Tour</span>
            </button>

            {/* Quick Preferences: Theme & Lang */}
            <div className="bg-surface-800/90 border border-line-strong rounded-[var(--radius-card)] p-3 flex items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <LanguageToggle />
                <ThemeToggleButton />
              </div>
              <button
                onClick={() => { setIsAIPreferencesOpen(true); setIsMobileMenuOpen(false); }}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 border border-line text-xs font-medium text-content-primary transition-colors cursor-pointer"
              >
                <Settings className="w-3.5 h-3.5 text-content-muted" />
                <span>Settings</span>
              </button>
            </div>

            {/* Auth / Account Action */}
            <div className="pt-1">
              {isAuthenticated ? (
                <button
                  onClick={() => {
                    logout();
                    setIsMobileMenuOpen(false);
                    showToast(t('header.loggedOut'), 'info');
                  }}
                  className="w-full flex items-center justify-center gap-2 p-2.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line text-xs font-medium text-danger-text hover:text-danger transition-colors cursor-pointer"
                >
                  <LogOut className="w-4 h-4" />
                  <span>{t('header.logOut')} ({user?.username})</span>
                </button>
              ) : (
                <button
                  onClick={() => { openAuthModal(); setIsMobileMenuOpen(false); }}
                  className="w-full flex items-center justify-center gap-2 p-2.5 rounded-full bg-accent hover:bg-accent-hover text-accent-on text-xs font-bold transition-all cursor-pointer shadow-sm"
                >
                  <span>{t('header.loginSignUp')}</span>
                  <ArrowRight className="w-4 h-4" />
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* V2 — Project Sidebar Drawer */}
      <ProjectSidebar
        isOpen={isSidebarOpen}
        onClose={() => setIsSidebarOpen(false)}
      />

      {/* Global Auth Modal Overlay */}
      <AuthModal
        isOpen={isAuthModalOpen}
        onClose={closeAuthModal}
      />

      {/* AI Preferences Modal */}
      <TeamModal isOpen={isTeamOpen} onClose={() => setIsTeamOpen(false)} />

      <AIPreferencesModal
        isOpen={isAIPreferencesOpen}
        onClose={() => setIsAIPreferencesOpen(false)}
      />

      {/* Global Quota Exhausted Modal */}
      <QuotaExhaustedModal />

      {/* Global Command Palette & Keyboard Shortcuts System */}
      <GlobalCommandPalette />
      <KeyboardShortcutsModal
        isOpen={isShortcutsOpen}
        onClose={() => setIsShortcutsOpen(false)}
      />

      {/* Daily Quota Warning Alert in Bottom-Right */}
      {(isCanvas || isCompile) && (
        <QuotaBottomRightAlert
          remainingPercent={remainingPercent}
          show={isAuthenticated}
        />
      )}
    </>
  );
}


interface QuotaAlertProps {
  remainingPercent: number;
  show: boolean;
}

function getQuotaRange(percent: number): number {
  if (percent === 0) return 0;
  if (percent <= 10) return 10;
  if (percent <= 25) return 25;
  if (percent <= 50) return 50;
  return 100;
}

function QuotaBottomRightAlert({ remainingPercent, show }: QuotaAlertProps) {
  const t = useT();
  const lang = useLangAttr();
  const [isDismissed, setIsDismissed] = useState(false);
  const currentRange = getQuotaRange(remainingPercent);
  const prevRange = useRef(currentRange);

  // Auto-reset dismissal if remainingPercent moves to a different range bracket (e.g. from 50 range to 25 range)
  useEffect(() => {
    if (prevRange.current !== currentRange) {
      setIsDismissed(false);
      prevRange.current = currentRange;
    }
  }, [currentRange]);

  if (!show || isDismissed || currentRange === 100) return null;

  let message = "";
  // Sarı/turuncu aile kaldırıldı — yalnızca gerçek kritik durum kırmızı,
  // geri kalanı nötr off-white ile (bkz. kullanıcı talimatı).
  let dotColor = "bg-content-muted";
  let borderClass = "border-line-strong";

  if (remainingPercent === 0) {
    message = t('header.quota.exhausted');
    dotColor = "bg-danger";
    borderClass = "border-danger/30";
  } else if (remainingPercent <= 10) {
    message = t('header.quota.low', { percent: remainingPercent });
    dotColor = "bg-danger";
    borderClass = "border-danger/30";
  } else {
    message = t('header.quota.remaining', { percent: remainingPercent });
    dotColor = "bg-content-muted";
    borderClass = "border-line-strong";
  }

  // Sağ altta; toast yığınının üstünde durması için bottom-24.
  return (
    <div lang={lang} className={`fixed bottom-24 right-6 z-toast flex items-center gap-2.5 px-4 py-2.5 rounded-[var(--radius-card)] bg-surface-800/95 border ${borderClass} shadow-xl backdrop-blur-md animate-in slide-in-from-bottom-3 duration-250 text-content-secondary font-sans text-xs select-none`}>
      <span className={`w-2 h-2 rounded-full ${dotColor} shrink-0`} />
      <span className="font-semibold">{message}</span>
      <button
        onClick={() => setIsDismissed(true)}
        className="ml-1.5 p-0.5 text-content-subtle hover:text-content-secondary transition-colors shrink-0 cursor-pointer active:scale-90"
        aria-label={t('header.quota.dismiss')}
      >
        <X className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}
