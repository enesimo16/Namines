'use client';

import { useState, useRef, useEffect, KeyboardEvent } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Database, Pencil, Check, ExternalLink, Sparkles, FolderOpen, Cloud, LogOut, Info, CheckCircle2, XCircle, AlertTriangle, X, Settings } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useToastStore } from '../../store/useToastStore';
import { useAIPolicyStore } from '../../store/useAIPolicyStore';
import { useQuotaStore } from '../../store/useQuotaStore';
import AuthModal from '../canvas/panels/AuthModal';
import AIPreferencesModal from '../canvas/panels/AIPreferencesModal';
import QuotaExhaustedModal from '../canvas/panels/QuotaExhaustedModal';
import ProjectSidebar from './ProjectSidebar';
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
  const inputRef = useRef<HTMLInputElement>(null);

  const { isAuthenticated, user, logout } = useAuthStore();
  const showToast = useToastStore(state => state.showToast);
  const syncWithCloud = useProjectHistoryStore(s => s.syncWithCloud);
  const fetchPolicy = useAIPolicyStore(s => s.fetchPolicy);
  const { dailyLimit, remaining } = useQuotaStore();
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
      <header className="flex items-center justify-between h-14 px-6 bg-[#0B1120]/80 backdrop-blur-md border-b border-blue-500/20 sticky top-0 z-50 w-full">
        {/* Left — Logo + Workspace + Project Name */}
        <div className="flex items-center gap-6">
          <button
            onClick={handleLogoClick}
            className="group flex items-center gap-2"
            title="Return to landing page and reset project"
            aria-label="Namines landing page"
          >
            <svg className="w-7 h-7 drop-shadow-[0_0_8px_rgba(99,102,241,0.4)]" viewBox="0 0 100 100" fill="none">
              <circle cx="50" cy="50" r="46" stroke="url(#circle-grad-header)" strokeWidth="3" fill="#090B11" />
              <path d="M20,62 C32,48 42,66 52,52 C62,38 72,56 84,42 L84,82 L20,82 Z" fill="url(#wave-grad-header)" opacity="0.8" />
              <path d="M16,68 C28,56 38,74 50,62 C62,50 72,68 84,56 L84,84 L16,84 Z" fill="url(#wave-grad-2-header)" opacity="0.4" />
              {/* Stars */}
              <circle cx="35" cy="30" r="1.5" fill="#FFF" />
              <circle cx="65" cy="25" r="2" fill="#FFF" />
              <circle cx="50" cy="20" r="1" fill="#FFF" />
              <circle cx="75" cy="35" r="1.2" fill="#FFF" />
              <defs>
                <linearGradient id="circle-grad-header" x1="0" y1="0" x2="100" y2="100">
                  <stop offset="0%" stopColor="#06b6d4" />
                  <stop offset="50%" stopColor="#818cf8" />
                  <stop offset="100%" stopColor="#a855f7" />
                </linearGradient>
                <linearGradient id="wave-grad-header" x1="50" y1="30" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#06b6d4" stopOpacity="0.8" />
                  <stop offset="100%" stopColor="#1e1b4b" stopOpacity="0.1" />
                </linearGradient>
                <linearGradient id="wave-grad-2-header" x1="50" y1="40" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#818cf8" stopOpacity="0.6" />
                  <stop offset="100%" stopColor="#0f172a" stopOpacity="0" />
                </linearGradient>
              </defs>
            </svg>
            <span className="font-bold tracking-widest text-lg bg-gradient-to-r from-blue-400 to-indigo-400 bg-clip-text text-transparent">NAMINES</span>
          </button>

          <div className="h-4 w-px bg-blue-500/20" />

          <button
            id="header-workspace-btn"
            onClick={() => setIsSidebarOpen(true)}
            className={`flex items-center gap-2 px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
              isSidebarOpen 
                ? 'bg-blue-500/10 text-blue-400' 
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/5'
            }`}
            title="View your saved projects"
          >
            <FolderOpen className="w-4 h-4" />
            <span>Workspace</span>
          </button>

          <div className="h-4 w-px bg-blue-500/20" />

          {/* Project Name Editor moved to left */}
          <div className="flex items-center">
            {isEditing ? (
              <div className="flex items-center gap-2">
                <input
                  ref={inputRef}
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  onBlur={commitEdit}
                  onKeyDown={handleKeyDown}
                  className="bg-zinc-900/50 border border-blue-500/30 text-white rounded px-2 py-1 text-sm w-48 focus:outline-none focus:border-blue-400"
                  maxLength={60}
                  autoFocus
                />
                <button
                  onClick={commitEdit}
                  className="text-emerald-400 hover:text-emerald-300 p-1"
                  aria-label="Save"
                >
                  <Check className="w-4 h-4" />
                </button>
              </div>
            ) : (
              <button
                onClick={startEditing}
                className="group flex items-center gap-2 text-zinc-300 hover:text-white px-2 py-1 rounded transition-colors"
                title="Edit project name"
              >
                <span className="text-sm font-medium">{projectName}</span>
                <Pencil className="w-3 h-3 opacity-0 group-hover:opacity-100 transition-opacity" />
              </button>
            )}
          </div>
        </div>

        {/* Right — Actions depending on path */}
        <div className="flex items-center gap-3">
          {isCanvas || isCompile ? (
            <button
                onClick={() => setIsAIPreferencesOpen(true)}
                title="Settings"
                aria-label="Open Settings"
                className="w-9 h-9 flex items-center justify-center rounded-full bg-white/5 hover:bg-cyan-500/5 border border-white/10 hover:border-cyan-500/35 text-zinc-300 hover:text-cyan-400 transition-all duration-200 cursor-pointer active:scale-95"
              >
                <Settings className="w-4 h-4" />
              </button>
          ) : isAuthenticated ? (
            <div 
              onClick={() => setIsAIPreferencesOpen(true)}
              className="flex items-center gap-3 pl-2 pr-3 py-1 bg-cyan-950/20 border border-cyan-500/20 hover:border-cyan-500/40 rounded-full select-none shadow-[0_2px_12px_rgba(6,182,212,0.08)] hover:bg-cyan-500/5 transition-all duration-200 cursor-pointer"
            >
              {/* Clean Avatar Orb */}
              <div 
                className="relative w-8 h-8 rounded-full bg-gradient-to-tr from-cyan-500 to-teal-500 border border-cyan-400/20 flex items-center justify-center text-[#06101a] text-[11px] font-bold uppercase tracking-wider select-none overflow-hidden shrink-0"
                title={user?.username}
              >
                {user?.username ? user.username.substring(0, 2).toUpperCase() : 'US'}
              </div>
              <div className="flex flex-col gap-0.5 justify-center">
                <span className="text-xs font-bold text-zinc-250 leading-none max-w-[95px] truncate tracking-wide" title={user?.username}>
                  {user?.username}
                </span>
                <span className="text-[8px] text-cyan-400/80 font-bold leading-none uppercase tracking-widest mt-0.5">
                  {user?.type === 'corporate' ? 'PRO MEMBER' : 'FREE MEMBER'}
                </span>
              </div>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  logout();
                  showToast('Logged out successfully.', 'info');
                }}
                className="p-1.5 text-zinc-400 hover:text-red-400 hover:bg-cyan-500/10 rounded-full transition-all duration-200 cursor-pointer ml-1.5 active:scale-95 shrink-0"
                title="Log Out"
              >
                <LogOut className="w-3.5 h-3.5" />
              </button>
            </div>
          ) : (
            <button
              id="auth-modal-trigger"
              onClick={openAuthModal}
              className="flex items-center justify-center py-1.5 px-4 rounded-xl border border-indigo-500/30 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-200 hover:text-white text-[10px] font-extrabold uppercase tracking-wider transition-all shadow-[0_0_12px_rgba(99,102,241,0.15)] cursor-pointer"
            >
              <span>Login / Sign Up</span>
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
      <AIPreferencesModal
        isOpen={isAIPreferencesOpen}
        onClose={() => setIsAIPreferencesOpen(false)}
      />

      {/* Global Quota Exhausted Modal */}
      <QuotaExhaustedModal />

      {/* Daily Quota Warning Alert in Bottom-Left */}
      {(isCanvas || isCompile) && (
        <QuotaBottomLeftAlert 
          remainingPercent={remainingPercent} 
          isFreeUser={isAuthenticated && user?.type !== 'corporate'} 
        />
      )}
    </>
  );
}


interface QuotaAlertProps {
  remainingPercent: number;
  isFreeUser: boolean;
}

function getQuotaRange(percent: number): number {
  if (percent === 0) return 0;
  if (percent <= 10) return 10;
  if (percent <= 25) return 25;
  if (percent <= 50) return 50;
  return 100;
}

function QuotaBottomLeftAlert({ remainingPercent, isFreeUser }: QuotaAlertProps) {
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

  if (!isFreeUser || isDismissed || currentRange === 100) return null;

  let message = "";
  let dotColor = "bg-amber-400";
  let borderClass = "border-amber-500/20";
  let shadowClass = "shadow-amber-500/5";

  if (remainingPercent === 0) {
    message = "Finished. All AI features have switched to local.";
    dotColor = "bg-rose-500";
    borderClass = "border-rose-500/30";
    shadowClass = "shadow-rose-500/15";
  } else if (remainingPercent <= 10) {
    message = `AI Credits: ${remainingPercent}% remaining. Switching to local soon.`;
    dotColor = "bg-red-500 animate-pulse";
    borderClass = "border-red-500/30";
    shadowClass = "shadow-red-500/15";
  } else if (remainingPercent <= 25) {
    message = `AI Credits: ${remainingPercent}% remaining.`;
    dotColor = "bg-orange-500";
    borderClass = "border-orange-500/20";
    shadowClass = "shadow-orange-500/5";
  } else {
    message = `AI Credits: ${remainingPercent}% remaining.`;
    dotColor = "bg-amber-400";
    borderClass = "border-amber-500/20";
    shadowClass = "shadow-amber-500/5";
  }

  return (
    <div className={`fixed bottom-6 left-6 z-[9999] flex items-center gap-2.5 px-4.5 py-2.5 rounded-full bg-slate-950/90 border ${borderClass} shadow-xl ${shadowClass} backdrop-blur-md animate-in slide-in-from-bottom-3 duration-250 text-zinc-300 font-sans text-xs select-none`}>
      <span className={`w-2 h-2 rounded-full ${dotColor} shrink-0`} />
      <span className="font-semibold">{message}</span>
      <button 
        onClick={() => setIsDismissed(true)} 
        className="ml-1.5 p-0.5 text-zinc-500 hover:text-zinc-300 transition-colors shrink-0 cursor-pointer active:scale-90"
        aria-label="Dismiss alert"
      >
        <X className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}
