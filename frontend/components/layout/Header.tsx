'use client';

import { useState, useRef, useEffect, KeyboardEvent } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import Link from 'next/link';
import { Pencil, Check, FolderOpen, LogOut, X, Settings, Users, ArrowRight } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useToastStore } from '../../store/useToastStore';
import { useAIPolicyStore } from '../../store/useAIPolicyStore';
import { useQuotaStore } from '../../store/useQuotaStore';
import AuthModal from '../canvas/panels/AuthModal';
import AIPreferencesModal from '../canvas/panels/AIPreferencesModal';
import TeamModal from './TeamModal';
import QuotaExhaustedModal from '../canvas/panels/QuotaExhaustedModal';
import ProjectSidebar from './ProjectSidebar';
import Logo from './Logo';
import HomeBanner from '../landing/HomeBanner';
import ThemeToggleButton from '../landing/ThemeToggleButton';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { activateOnKey } from '../../lib/a11y';

const HOME_NAV_ITEMS = [
  { id: 'how-it-works', label: 'How it works' },
  { id: 'engines', label: 'Engines' },
  { id: 'zero-drift', label: 'Zero Drift' },
  { id: 'spotlights', label: 'Architecture' },
  { id: 'builder-grid', label: 'Ecosystem' },
  { id: 'security', label: 'Security' },
];

export default function Header() {
  const router = useRouter();
  const pathname = usePathname();
  const { projectName, setProjectName, resetProject } = useSchemaStore();
  
  const isCanvas = pathname === '/canvas';
  const isCompile = pathname === '/compile';
  // Genel tanıtım sayfası (marketing) — burada proje/workspace bağlamı yok,
  // o yüzden Header buraya özel sadeleştirilmiş bir gezinme gösteriyor
  // (bkz. aşağıdaki `isHome` dallanmaları). Diğer tüm rotalarda Header
  // AYNEN eskisi gibi davranmaya devam ediyor.
  const isHome = pathname === '/';

  const [activeSection, setActiveSection] = useState<string>('');
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(projectName);
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);
  const { isOpen: isAuthModalOpen, open: openAuthModal, close: closeAuthModal } = useAuthModalStore();
  const [isAIPreferencesOpen, setIsAIPreferencesOpen] = useState(false);
  const [isTeamOpen, setIsTeamOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

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

  const { isAuthenticated, user, logout } = useAuthStore();
  const showToast = useToastStore(state => state.showToast);
  const syncWithCloud = useProjectHistoryStore(s => s.syncWithCloud);
  // Proje geçmişi IndexedDB'den ASENKRON hidrate oluyor. Bu bayrak olmadan
  // aşağıdaki senkron efekti, oturum açık olsa bile HENÜZ YÜKLENMEMİŞ (boş)
  // bir proje listesiyle çalışıp sessizce hiçbir şey göndermiyordu.
  const hasHydrated = useProjectHistoryStore(s => s.hasHydrated);
  const fetchPolicy = useAIPolicyStore(s => s.fetchPolicy);
  const { dailyLimit, remaining, plan } = useQuotaStore();
  // Dev hesabi digerlerine gorunmuyor: rozet DEV yaziyor ama bu yalnizca o
  // hesabin kendi ekraninda goruluyor, baska kullaniciya asla gitmiyor.
  const planLabel = plan === 'Dev' ? 'DEV'
    : plan === 'Team' ? 'TEAM MEMBER'
    : plan === 'Pro' ? 'PRO MEMBER'
    : 'FREE MEMBER';
  const remainingPercent = dailyLimit > 0 ? Math.min(100, Math.max(0, Math.round((remaining / dailyLimit) * 100))) : 100;

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

  const startEditing = () => {
    setDraft(projectName);
    setIsEditing(true);
    setTimeout(() => inputRef.current?.select(), 0);
  };

  const commitEdit = () => {
    const trimmed = draft.trim() || 'New Project';
    setProjectName(trimmed);
    setIsEditing(false);
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') commitEdit();
    if (e.key === 'Escape') { setIsEditing(false); setDraft(projectName); }
  };

  const handleLogoClick = () => {
    resetProject();
    router.push('/');
  };

  return (
    <>
      {/* Sticky DEĞİL, normal akışta — bu yüzden aşağı kaydırınca kendiliğinden
          görünümden çıkıyor (JS scroll dinleyicisi gerekmiyor), sticky
          <header> ise altında top:0'a yapışıp kalıyor. Yalnızca tanıtım
          sayfasında (`isHome`): app içi ekranlarda (canvas/compile/...)
          promosyon şeridi göstermek yersiz olurdu. */}
      {isHome && <HomeBanner />}

      <header
        className={`flex items-center justify-between h-14 px-3 sm:px-6 backdrop-blur-md sticky top-0 z-50 w-full transition-colors ${
          isHome
            ? 'bg-surface-900/90 border-b border-surface-500'
            : 'bg-surface-800/85 border-b border-content-primary/10'
        }`}
      >
        {/* Left — Logo + Global Nav Links */}
        <nav aria-label="Global" className="flex items-center gap-2 sm:gap-6 min-w-0">
          <button
            onClick={handleLogoClick}
            className="shrink-0"
            title="Return to homepage"
            aria-label="Namines homepage"
          >
            <Logo size="sm" />
          </button>

          {isHome ? (
            <div className="hidden md:flex items-center gap-5 lg:gap-6 ml-3">
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
                    <span>{item.label}</span>
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
                Demo
              </Link>
            </div>
          ) : (
            <>
              <div className="h-4 w-px bg-surface-500 shrink-0" />

              {/* Workspace / Team — YALNIZCA İKON (kullanıcı talebi: "üstteki
                  workspace ve team yazılarını kaldırıp sadece ikonlarla
                  tanımlayabiliriz"). Metin `title` + `aria-label`'da yaşamaya
                  devam ediyor: fare üstüne gelince tooltip, ekran okuyucuda tam ad
                  — görsel etiket gitti, erişilebilirlik gitmedi.

                  İkonların kendisi zaten ayırt edici: klasör = kaydedilmiş
                  projeler, insanlar = ekip. Bunlar araç çubuğundaki 9 soyut ikonun
                  aksine evrensel karşılığı olan iki simge, o yüzden burada
                  etiketsiz kalmaları güvenli. */}
              <button
                id="header-workspace-btn"
                onClick={() => setIsSidebarOpen(true)}
                className={`tap-44 flex items-center justify-center w-9 h-9 rounded-[var(--radius-control)] transition-colors shrink-0 ${
                  isSidebarOpen
                    ? 'bg-white/[0.08] text-content-primary'
                    : 'text-content-muted hover:text-content-primary hover:bg-white/[0.04]'
                }`}
                title="Workspace — your saved projects"
                aria-label="Workspace"
              >
                <FolderOpen className="w-4 h-4" />
              </button>

              {/* Ekip — yalnızca giriş yapmış kullanıcıda. Team planı olmayanlarda da
                  görünüyor ama panel "bu plan tek kişilik" diyor: özelliği tamamen
                  gizlemek, Team'in ne sattığını görünmez kılardı. */}
              {isAuthenticated && (
                <button
                  onClick={() => setIsTeamOpen(true)}
                  className={`tap-44 flex items-center justify-center w-9 h-9 rounded-[var(--radius-control)] transition-colors shrink-0 ${
                    isTeamOpen
                      ? 'bg-white/[0.08] text-content-primary'
                      : 'text-content-muted hover:text-content-primary hover:bg-white/[0.04]'
                  }`}
                  title="Team — members and shared projects"
                  aria-label="Team"
                >
                  <Users className="w-4 h-4" />
                </button>
              )}

              <div className="h-4 w-px bg-surface-500 shrink-0 hidden sm:block" />

              {/* Project Name Editor moved to left — dar ekranda gizli, yer kaplamasın */}
              <div className="hidden md:flex items-center">
                {isEditing ? (
                  <div className="flex items-center gap-2">
                    <input
                      ref={inputRef}
                      aria-label="Project name"
                      value={draft}
                      onChange={(e) => setDraft(e.target.value)}
                      onBlur={commitEdit}
                      onKeyDown={handleKeyDown}
                      className="bg-surface-700/70 border border-content-primary/15 text-content-primary rounded-[var(--radius-control)] px-2 py-1 text-sm w-48 focus:outline-none focus:border-focus-ring"
                      maxLength={60}
                      autoFocus
                    />
                    <button
                      onClick={commitEdit}
                      className="text-success-text hover:text-success-text p-1"
                      aria-label="Save"
                    >
                      <Check className="w-4 h-4" />
                    </button>
                  </div>
                ) : (
                  <button
                    onClick={startEditing}
                    className="group flex items-center gap-2 text-content-primary hover:text-content-primary px-2 py-1 rounded-[var(--radius-control)] transition-colors"
                    title="Edit project name"
                  >
                    <span className="text-sm font-medium max-w-[160px] truncate">{projectName}</span>
                    <Pencil className="w-3 h-3 opacity-0 group-hover:opacity-100 transition-opacity" />
                  </button>
                )}
              </div>
            </>
          )}
        </nav>

        {/* Right — Actions depending on path */}
        <div className="flex items-center gap-2 sm:gap-3 shrink-0">
          {!isCanvas && !isCompile && <ThemeToggleButton />}
          {isHome && !isAuthenticated ? (
            <>
              <Link
                href="/demo"
                className="hidden lg:inline text-xs font-medium text-content-secondary hover:text-content-primary transition-colors px-2"
              >
                Explore Demo
              </Link>
              <button
                onClick={openAuthModal}
                className="hidden sm:inline text-xs font-medium text-content-secondary hover:text-accent-text transition-colors px-2"
              >
                Sign In
              </button>
              <Link
                href="/new"
                className="inline-flex items-center gap-1.5 py-1.5 px-3.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-xs font-bold transition-all cursor-pointer whitespace-nowrap shadow-sm shadow-accent/15"
              >
                Get Started
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </>
          ) : isCanvas || isCompile ? (
            <button
                onClick={() => setIsAIPreferencesOpen(true)}
                title="Settings"
                aria-label="Open Settings"
                className="lg:hidden w-9 h-9 flex items-center justify-center rounded-full bg-white/[0.04] hover:bg-content-primary/15 border border-content-primary/10 hover:border-white/25 text-content-primary hover:text-content-primary transition-all duration-200 cursor-pointer active:scale-95"
              >
                <Settings className="w-4 h-4" />
              </button>
          ) : isAuthenticated ? (
            <div
              onClick={() => setIsAIPreferencesOpen(true)}
              onKeyDown={activateOnKey(() => setIsAIPreferencesOpen(true))}
              role="button"
              tabIndex={0}
              aria-haspopup="dialog"
              aria-label="Open account and AI settings"
              className="flex items-center gap-2 sm:gap-3 pl-1.5 sm:pl-2 pr-1.5 sm:pr-3 py-1 bg-white/[0.06] border border-content-primary/15 hover:border-white/25 rounded-full select-none shadow-[0_2px_12px_color-mix(in srgb, var(--color-accent) 10%, transparent)] hover:bg-white/[0.08] transition-all duration-200 cursor-pointer"
            >
              {/* Clean Avatar Orb */}
              <div
                className="relative w-8 h-8 rounded-full bg-gradient-to-tr from-accent-hover to-accent border border-focus-ring/30 flex items-center justify-center text-content-primary text-[11px] font-bold uppercase tracking-wider select-none overflow-hidden shrink-0"
                title={user?.username}
              >
                {user?.username ? user.username.substring(0, 2).toUpperCase() : 'US'}
              </div>
              <div className="hidden sm:flex flex-col gap-0.5 justify-center">
                <span className="text-xs font-bold text-content-primary leading-none max-w-[95px] truncate tracking-wide" title={user?.username}>
                  {user?.username}
                </span>
                <span className="text-micro text-content-primary font-bold leading-none uppercase tracking-widest mt-0.5">
                  {planLabel}
                </span>
              </div>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  logout();
                  showToast('Logged out successfully.', 'info');
                }}
                className="tap-44 p-1.5 text-content-muted hover:text-danger-text hover:bg-danger-subtle rounded-full transition-all duration-200 cursor-pointer ml-0 sm:ml-1.5 active:scale-95 shrink-0"
                title="Log Out"
                aria-label="Log Out"
              >
                <LogOut className="w-3.5 h-3.5" />
              </button>
            </div>
          ) : (
            <button
              id="auth-modal-trigger"
              onClick={openAuthModal}
              className="flex items-center justify-center py-1.5 px-3 sm:px-4 rounded-[var(--radius-card)] bg-content-primary hover:bg-content-primary-hover text-surface-900 text-[10px] font-extrabold uppercase tracking-wider transition-all cursor-pointer whitespace-nowrap"
            >
              <span className="hidden sm:inline">Login / Sign Up</span>
              <span className="inline sm:hidden">Login</span>
            </button>
          )}
        </div>
      </header>

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
  let borderClass = "border-content-primary/10";

  if (remainingPercent === 0) {
    message = "Out of tokens — minimum AI is active. All free features stay available.";
    dotColor = "bg-danger";
    borderClass = "border-danger/30";
  } else if (remainingPercent <= 10) {
    message = `AI tokens: ${remainingPercent}% left — switching to minimum AI soon.`;
    dotColor = "bg-danger";
    borderClass = "border-danger/30";
  } else {
    message = `AI tokens: ${remainingPercent}% left.`;
    dotColor = "bg-content-muted";
    borderClass = "border-content-primary/10";
  }

  // Sağ altta; toast yığınının üstünde durması için bottom-24.
  return (
    <div className={`fixed bottom-24 right-6 z-[9998] flex items-center gap-2.5 px-4.5 py-2.5 rounded-full bg-surface-800/95 border ${borderClass} backdrop-blur-md animate-in slide-in-from-bottom-3 duration-250 text-content-secondary font-sans text-xs select-none`}>
      <span className={`w-2 h-2 rounded-full ${dotColor} shrink-0`} />
      <span className="font-semibold">{message}</span>
      <button
        onClick={() => setIsDismissed(true)}
        className="ml-1.5 p-0.5 text-content-subtle hover:text-content-secondary transition-colors shrink-0 cursor-pointer active:scale-90"
        aria-label="Dismiss alert"
      >
        <X className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}
