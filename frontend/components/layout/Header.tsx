'use client';

import { useState, useRef, useEffect, KeyboardEvent } from 'react';
import { useRouter } from 'next/navigation';
import { Database, Pencil, Check, ExternalLink, Sparkles, FolderOpen, Cloud, LogOut, Info, CheckCircle2, XCircle, AlertTriangle, X } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useToastStore } from '../../store/useToastStore';
import AuthModal from '../canvas/panels/AuthModal';
import ProjectSidebar from './ProjectSidebar';

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
    const trimmed = draft.trim() || 'Yeni Proje';
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
            className="group flex items-center gap-1"
            title="Ana sayfaya dön ve projeyi sıfırla"
            aria-label="Namines ana sayfa"
          >
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
            title="Kayıtlı projelerini görüntüle"
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
                  aria-label="Kaydet"
                >
                  <Check className="w-4 h-4" />
                </button>
              </div>
            ) : (
              <button
                onClick={startEditing}
                className="group flex items-center gap-2 text-zinc-300 hover:text-white px-2 py-1 rounded transition-colors"
                title="Proje adını düzenle"
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
                  {user?.type === 'corporate' ? 'Kurumsal' : 'Bireysel'}
                </span>
              </div>
              <button
                onClick={() => {
                  logout();
                  showToast('Oturum kapatıldı, bulut yedekleme pasif.', 'info');
                }}
                className="p-1.5 text-zinc-400 hover:text-white hover:bg-white/5 rounded-full transition-all duration-200 cursor-pointer ml-1 active:scale-95 border border-transparent hover:border-zinc-800"
                title="Oturumu Kapat"
              >
                <LogOut className="w-3.5 h-3.5 text-zinc-400 hover:text-indigo-400 transition-colors drop-shadow-[0_0_2px_rgba(99,102,241,0.2)]" />
              </button>
            </div>
          ) : (
            <button
              onClick={() => setIsAuthModalOpen(true)}
              className="flex items-center gap-1.5 py-1.5 px-3.5 rounded-xl border border-indigo-500/30 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-200 hover:text-white text-[10px] font-extrabold uppercase tracking-wider transition-all shadow-[0_0_12px_rgba(99,102,241,0.15)] cursor-pointer"
            >
              <Cloud className="w-3.5 h-3.5 text-indigo-400 animate-pulse" />
              <span>Giriş Yap / Kayıt Ol</span>
              <span className="relative flex h-1.5 w-1.5">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-indigo-400 opacity-75"></span>
                <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-indigo-500"></span>
              </span>
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
