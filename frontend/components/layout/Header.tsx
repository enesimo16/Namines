'use client';

import { useState, useRef, useEffect, KeyboardEvent } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Database, Pencil, Check, ExternalLink, Sparkles, FolderOpen, Cloud, LogOut, Info, CheckCircle2, XCircle, AlertTriangle, X, Settings, Users } from 'lucide-react';
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
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';

export default function Header() {
  const router = useRouter();
  const pathname = usePathname();
  const { projectName, setProjectName, resetProject } = useSchemaStore();
  
  const isCanvas = pathname === '/canvas';
  const isCompile = pathname === '/compile';

  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(projectName);
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);
  const { isOpen: isAuthModalOpen, open: openAuthModal, close: closeAuthModal } = useAuthModalStore();
  const [isAIPreferencesOpen, setIsAIPreferencesOpen] = useState(false);
  const [isTeamOpen, setIsTeamOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const { isAuthenticated, user, logout } = useAuthStore();
  const showToast = useToastStore(state => state.showToast);
  const syncWithCloud = useProjectHistoryStore(s => s.syncWithCloud);
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
    if (isAuthenticated) {
      if (syncWithCloud) syncWithCloud();
      fetchPolicy();
      useQuotaStore.getState().fetchQuota();
    }
  }, [isAuthenticated, syncWithCloud, fetchPolicy]);

  // Event listener to open AI settings modal from other components/hooks
  useEffect(() => {
    const handleOpenSettings = () => setIsAIPreferencesOpen(true);
    window.addEventListener('namines:open-ai-settings', handleOpenSettings);
    return () => window.removeEventListener('namines:open-ai-settings', handleOpenSettings);
  }, []);

  // draft'ı store ile senkronda tut
  useEffect(() => {
    if (!isEditing) setDraft(projectName);
  }, [projectName, isEditing]);

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
      <header className="flex items-center justify-between h-14 px-3 sm:px-6 bg-surface-800/85 backdrop-blur-md border-b border-content-primary/10 sticky top-0 z-50 w-full">
        {/* Left — Logo + Workspace + Project Name

            `<nav>` landmark'ı: ölçüldü, sayfada `<nav>` sayısı 0'dı. Ekran
            okuyucu kullanıcısı gezinme bağlantılarını içerikten ayırt
            edemiyordu (bkz. UI_UX_PRODUCT_AUDIT.md §4 / Y2). Bu şerit
            GLOBAL gezinme — alanlar arası geçiş; araç çubuğu (tuval eylemleri)
            ve bilgi paneli (proje içi) ayrı katmanlar, onlar nav değil. */}
        <nav aria-label="Global" className="flex items-center gap-2 sm:gap-6 min-w-0">
          <button
            onClick={handleLogoClick}
            className="shrink-0"
            title="Return to landing page and reset project"
            aria-label="Namines landing page"
          >
            <Logo size="sm" />
          </button>

          <div className="h-4 w-px bg-surface-500 shrink-0" />

          <button
            id="header-workspace-btn"
            onClick={() => setIsSidebarOpen(true)}
            className={`tap-44 flex items-center gap-2 px-2.5 sm:px-3 py-1.5 rounded-[var(--radius-control)] text-sm font-medium transition-colors shrink-0 ${
              isSidebarOpen
                ? 'bg-white/[0.08] text-content-primary'
                : 'text-content-muted hover:text-content-primary hover:bg-white/[0.04]'
            }`}
            title="View your saved projects"
          >
            <FolderOpen className="w-4 h-4" />
            <span className="hidden sm:inline">Workspace</span>
          </button>

          {/* Ekip — yalnızca giriş yapmış kullanıcıda. Team planı olmayanlarda da
              görünüyor ama panel "bu plan tek kişilik" diyor: özelliği tamamen
              gizlemek, Team'in ne sattığını görünmez kılardı. */}
          {isAuthenticated && (
            <button
              onClick={() => setIsTeamOpen(true)}
              className={`tap-44 flex items-center gap-2 px-2.5 sm:px-3 py-1.5 rounded-[var(--radius-control)] text-sm font-medium transition-colors shrink-0 ${
                isTeamOpen
                  ? 'bg-white/[0.08] text-content-primary'
                  : 'text-content-muted hover:text-content-primary hover:bg-white/[0.04]'
              }`}
              title="Team members and shared projects"
              aria-label="Team"
            >
              <Users className="w-4 h-4" />
              <span className="hidden lg:inline">Team</span>
            </button>
          )}

          <div className="h-4 w-px bg-surface-500 shrink-0 hidden sm:block" />

          {/* Project Name Editor moved to left — dar ekranda gizli, yer kaplamasın */}
          <div className="hidden md:flex items-center">
            {isEditing ? (
              <div className="flex items-center gap-2">
                <input
                  ref={inputRef}
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
                  className="text-success hover:text-success-text p-1"
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
        </nav>

        {/* Right — Actions depending on path */}
        <div className="flex items-center gap-2 sm:gap-3 shrink-0">
          {isCanvas || isCompile ? (
            <button
                onClick={() => setIsAIPreferencesOpen(true)}
                title="Settings"
                aria-label="Open Settings"
                className="w-9 h-9 flex items-center justify-center rounded-full bg-white/[0.04] hover:bg-content-primary/15 border border-content-primary/10 hover:border-white/25 text-content-primary hover:text-content-primary transition-all duration-200 cursor-pointer active:scale-95"
              >
                <Settings className="w-4 h-4" />
              </button>
          ) : isAuthenticated ? (
            <div
              onClick={() => setIsAIPreferencesOpen(true)}
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
                className="tap-44 p-1.5 text-content-muted hover:text-danger hover:bg-danger-subtle rounded-full transition-all duration-200 cursor-pointer ml-0 sm:ml-1.5 active:scale-95 shrink-0"
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
    message = "Token bitti — minimum AI aktif. Tüm ücretsiz özellikler açık.";
    dotColor = "bg-danger";
    borderClass = "border-danger/30";
  } else if (remainingPercent <= 10) {
    message = `AI token: %${remainingPercent} kaldı — birazdan minimum AI'ya geçilecek.`;
    dotColor = "bg-danger";
    borderClass = "border-danger/30";
  } else {
    message = `AI token: %${remainingPercent} kaldı.`;
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
