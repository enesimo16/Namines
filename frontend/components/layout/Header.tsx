'use client';

import { useState, useRef, useEffect, KeyboardEvent } from 'react';
import { useRouter } from 'next/navigation';
import { Database, Pencil, Check, ExternalLink, Sparkles, FolderOpen, Cloud, LogOut, Info, CheckCircle2, XCircle, AlertTriangle, X } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useToastStore } from '../../store/useToastStore';
import AuthModal from '../canvas/panels/AuthModal';
import ProjectSidebar from './ProjectSidebar';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';

export default function Header() {
  const router = useRouter();
  const { projectName, setProjectName, resetProject } = useSchemaStore();

  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(projectName);
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);
  const [isAuthModalOpen, setIsAuthModalOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const { isAuthenticated, user, logout } = useAuthStore();
  const showToast = useToastStore(state => state.showToast);
  const syncWithCloud = useProjectHistoryStore(s => s.syncWithCloud);

  // Sync projects with cloud when authenticated
  useEffect(() => {
    if (isAuthenticated && syncWithCloud) {
      syncWithCloud();
    }
  }, [isAuthenticated, syncWithCloud]);

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

        {/* Right — Cloud Sync Status Indicator / Login Trigger */}
        <div className="flex items-center gap-3">
          {isAuthenticated ? (
            <div className="flex items-center gap-3 pl-2 pr-3 py-1 bg-zinc-950/80 border border-zinc-800/80 rounded-full select-none shadow-[0_4px_25px_rgba(0,0,0,0.5),0_0_15px_rgba(99,102,241,0.03)] backdrop-blur-md hover:border-zinc-700 transition-all duration-300">
              {/* 3D Coded Glowing Planet Avatar */}
              <div 
                className="relative w-8 h-8 rounded-full bg-gradient-to-tr from-blue-600 via-indigo-600 to-purple-600 shadow-[inset_-3px_-3px_8px_rgba(0,0,0,0.85),0_0_12px_rgba(99,102,241,0.5)] border border-indigo-400/20 flex items-center justify-center text-white text-[10.5px] font-black uppercase tracking-wider select-none overflow-hidden"
                title={user?.username}
              >
                {/* Specular Highlight Layer */}
                <div className="absolute inset-0 bg-[radial-gradient(circle_at_25%_25%,rgba(255,255,255,0.22),transparent_45%)] pointer-events-none" />
                {user?.username ? user.username.substring(0, 2).toUpperCase() : 'US'}
              </div>
              <div className="flex flex-col gap-0.5 justify-center">
                <span className="text-xs font-black text-white leading-none max-w-[85px] truncate tracking-wide" title={user?.username}>
                  {user?.username}
                </span>
                <span className="text-[7.5px] text-zinc-500 font-black leading-none uppercase tracking-widest">
                  {user?.type === 'corporate' ? 'Corporate' : 'Personal'}
                </span>
              </div>
              <button
                onClick={() => {
                  logout();
                  showToast('Logged out, cloud backup disabled.', 'info');
                }}
                className="p-1.5 text-zinc-400 hover:text-white hover:bg-white/5 rounded-full transition-all duration-200 cursor-pointer ml-1 active:scale-95 border border-transparent hover:border-zinc-800"
                title="Log Out"
              >
                <LogOut className="w-3.5 h-3.5 text-zinc-400 hover:text-indigo-400 transition-colors drop-shadow-[0_0_2px_rgba(99,102,241,0.2)]" />
              </button>
            </div>
          ) : (
            <button
              onClick={() => setIsAuthModalOpen(true)}
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
        onClose={() => setIsAuthModalOpen(false)}
      />

      {/* Global Unified Toast Notification Panel (Bottom-Left) */}
      <ToastContainer />
    </>
  );
}

function ToastContainer() {
  const { message, type, hideToast } = useToastStore();

  if (!message) return null;

  let bgClass = 'from-[#09111F]/98 to-[#0D182A]/98 text-white border-indigo-500/40 shadow-[0_0_30px_rgba(99,102,241,0.3)]';
  let Icon = Info;
  let iconColor = 'text-indigo-400';

  if (type === 'success') {
    bgClass = 'from-[#09111F]/98 to-emerald-950/98 text-white border-emerald-500/40 shadow-[0_0_30px_rgba(16,185,129,0.3)]';
    Icon = CheckCircle2;
    iconColor = 'text-emerald-400';
  } else if (type === 'error') {
    bgClass = 'from-[#09111F]/98 to-rose-950/98 text-white border-rose-500/40 shadow-[0_0_30px_rgba(239,68,68,0.3)]';
    Icon = XCircle;
    iconColor = 'text-rose-400';
  } else if (type === 'warning') {
    bgClass = 'from-[#09111F]/98 to-amber-950/98 text-white border-amber-500/40 shadow-[0_0_30px_rgba(245,158,11,0.3)]';
    Icon = AlertTriangle;
    iconColor = 'text-amber-400';
  }

  return (
    <div className="fixed bottom-6 right-6 z-[9999] animate-in slide-in-from-bottom-5 duration-300">
      <div className={`flex items-center gap-3 px-5 py-3.5 rounded-2xl border bg-gradient-to-r ${bgClass} backdrop-blur-xl max-w-sm shadow-2xl`}>
        <Icon className={`w-5 h-5 shrink-0 ${iconColor} animate-pulse drop-shadow-[0_0_4px_currentColor]`} />
        <span className="text-[13px] font-extrabold leading-relaxed tracking-wide text-white drop-shadow-[0_1px_2px_rgba(0,0,0,0.8)]">{message}</span>
        <button 
          onClick={hideToast} 
          className="ml-3 text-zinc-400 hover:text-white transition-colors shrink-0 cursor-pointer active:scale-90"
        >
          <X className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}
