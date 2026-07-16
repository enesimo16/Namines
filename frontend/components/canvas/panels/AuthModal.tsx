'use client';

import React, { useState, useRef } from 'react';
import { X, Lock, Mail, User, Building2, CheckCircle2, AlertTriangle, ShieldCheck, Sparkles } from 'lucide-react';
import { useAuthStore } from '../../../store/useAuthStore';
import { useToastStore } from '../../../store/useToastStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { authService } from '../../../services/api';
import GuestSchemaMigrationModal from './GuestSchemaMigrationModal';
import { useFocusTrap } from '../../../hooks/useFocusTrap';

interface AuthModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function AuthModal({ isOpen, onClose }: AuthModalProps) {
  const { setAuth } = useAuthStore();
  const { showToast } = useToastStore();
  const { projects } = useProjectHistoryStore();
  const modalRef = useRef<HTMLDivElement>(null);

  const [isLogin, setIsLogin] = useState(true);
  const [userType, setUserType] = useState<'individual' | 'corporate'>('individual');
  
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [companyName, setCompanyName] = useState('');
  
  const [isLoading, setIsLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [showMigration, setShowMigration] = useState(false);

  // trapping focus when open
  useFocusTrap(isOpen && !showMigration, modalRef);

  const handleSyncLocalProjects = async () => {
    if (projects.length === 0) return;
    
    try {
      const projectsToSync = projects.map(p => ({
        id: p.id,
        name: p.name,
        dbType: p.dbType,
        schemaJson: JSON.stringify(p.schema),
        nodePositionsJson: JSON.stringify(p.nodePositions)
      }));

      await authService.syncProjects(projectsToSync);
      showToast(`${projects.length} local projects successfully synchronized with your cloud account!`, 'success');
    } catch (err: any) {
      console.error('Sync error:', err);
      showToast('An error occurred while uploading projects to the cloud.', 'warning');
    }
  };

  const handleSync = async () => {
    await handleSyncLocalProjects();
    await useProjectHistoryStore.getState().syncWithCloud();
    onClose();
    setShowMigration(false);
  };

  const handleDiscard = () => {
    useProjectHistoryStore.setState({ projects: [], activeProjectId: null });
    useSchemaStore.getState().resetProject();
    showToast('Guest projects discarded. Starting fresh!', 'info');
    onClose();
    setShowMigration(false);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setErrorMsg(null);

    try {
      if (isLogin) {
        // Login Flow
        const data = await authService.login(email, password);
        setAuth(data.token, {
          username: data.user.username,
          email: data.user.email,
          type: data.user.type,
          companyName: data.user.companyName
        }, data.quota);
        showToast('Logged in successfully. Cloud backup is active!', 'success');
        
        if (projects.length > 0) {
          setShowMigration(true);
        } else {
          await useProjectHistoryStore.getState().syncWithCloud();
          onClose();
        }
      } else {
        // Register Flow
        const data = await authService.register(
          email, 
          password, 
          username || undefined, 
          userType, 
          userType === 'corporate' ? companyName : undefined
        );
        
        setAuth(data.token, {
          username: data.user.username,
          email: data.user.email,
          type: data.user.type,
          companyName: data.user.companyName
        }, data.quota);
        
        showToast('Account created. Welcome!', 'success');
        
        if (projects.length > 0) {
          setShowMigration(true);
        } else {
          await useProjectHistoryStore.getState().syncWithCloud();
          onClose();
        }
      }
    } catch (err: any) {
      const msg = err.response?.data?.message || 'An error occurred. Please check your credentials.';
      setErrorMsg(msg);
      showToast(msg, 'error');
    } finally {
      setIsLoading(false);
    }
  };

  if (!isOpen) return null;

  if (showMigration) {
    return (
      <GuestSchemaMigrationModal
        isOpen={true}
        projects={projects}
        onSync={handleSync}
        onDiscard={handleDiscard}
      />
    );
  }

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4 bg-black/75 backdrop-blur-sm animate-fade-in">
      
      {/* Glow Backdrop Orbs */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[500px] h-[500px] bg-gradient-to-tr from-indigo-500/10 to-violet-500/10 rounded-full blur-[90px] pointer-events-none -z-10" />

      {/* Main Glassmorphic Container */}
      <div ref={modalRef} role="dialog" aria-modal="true" aria-labelledby="auth-modal-title" className="relative w-full max-w-lg bg-surface-900/90 backdrop-blur-2xl border border-indigo-500/20 rounded-3xl shadow-[0_20px_60px_rgba(0,0,0,0.8)] overflow-hidden flex flex-col p-8 pb-14 animate-in zoom-in-95 duration-200">

        {/* Header */}
        <div className="flex items-center justify-between pb-5 border-b border-indigo-500/10">
          <div className="flex items-center gap-2.5">
            <ShieldCheck className="w-6 h-6 text-indigo-400" />
            <h3 id="auth-modal-title" className="text-lg font-extrabold uppercase tracking-wider text-indigo-100">
              {isLogin ? 'Sign In' : 'Create Account'}
            </h3>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-white/5 rounded-lg text-zinc-400 hover:text-white transition-all cursor-pointer"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Info Banner */}
        <div className="my-5 p-3.5 rounded-xl bg-indigo-500/10 border border-indigo-500/20 flex gap-3 items-start">
          <CheckCircle2 className="w-5 h-5 text-indigo-400 shrink-0 mt-0.5" />
          <p className="text-sm text-indigo-200/90 leading-relaxed font-medium">
            Your schemas and branch history are safely stored in the cloud and never lost.
          </p>
        </div>

        {/* Unified capsule tab selector (login & sign up) */}
        <div className="flex bg-zinc-950/60 p-1.5 rounded-2xl border border-zinc-800/80 mb-6">
          <button
            type="button"
            onClick={() => { setIsLogin(true); setErrorMsg(null); }}
            className={`flex-1 py-2.5 text-center text-sm font-bold rounded-xl tracking-wide transition-all duration-200 ${
              isLogin
                ? 'bg-[#4F46E5] text-white shadow-[0_2px_10px_rgba(79,70,229,0.4)]'
                : 'bg-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            Login
          </button>
          <button
            type="button"
            onClick={() => { setIsLogin(false); setErrorMsg(null); }}
            className={`flex-1 py-2.5 text-center text-sm font-bold rounded-xl tracking-wide transition-all duration-200 ${
              !isLogin
                ? 'bg-[#4F46E5] text-white shadow-[0_2px_10px_rgba(79,70,229,0.4)]'
                : 'bg-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            Sign Up
          </button>
        </div>

        {/* Error Message Display */}
        {errorMsg && (
          <div className="mb-4 px-4 py-3 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm font-medium flex items-center gap-2.5">
            <AlertTriangle className="w-5 h-5 shrink-0 animate-pulse text-rose-400" />
            <span>{errorMsg}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="flex flex-col gap-3.5">
          
          {/* Account Tier Toggle (Only on Register) - Cyan outline border matching stitch_screen_2.png */}
          {!isLogin && (
            <div className="flex flex-col gap-1.5 mb-1">
              <label className="text-sm font-semibold text-indigo-300 uppercase tracking-wider px-1">Account Type</label>
              <div className="grid grid-cols-2 gap-3">
                <button
                  type="button"
                  onClick={() => setUserType('individual')}
                  className={`py-2 px-3 text-center border rounded-xl flex flex-col items-center justify-center gap-0.5 transition-all ${
                    userType === 'individual'
                      ? 'bg-indigo-500/5 border-cyan-500 text-cyan-200 shadow-[0_0_15px_rgba(6,182,212,0.15)]'
                      : 'bg-zinc-950/20 border-zinc-800/80 text-zinc-500 hover:text-zinc-300 hover:border-zinc-700'
                  }`}
                >
                  <span className="text-sm font-extrabold tracking-wide">Personal</span>
                  <span className="text-[11px] opacity-75 leading-none mt-0.5">For individual developers</span>
                </button>
                <button
                  type="button"
                  onClick={() => setUserType('corporate')}
                  className={`py-2 px-3 text-center border rounded-xl flex flex-col items-center justify-center gap-0.5 transition-all ${
                    userType === 'corporate'
                      ? 'bg-indigo-500/5 border-cyan-500 text-cyan-200 shadow-[0_0_15px_rgba(6,182,212,0.15)]'
                      : 'bg-zinc-950/20 border-zinc-800/80 text-zinc-500 hover:text-zinc-300 hover:border-zinc-700'
                  }`}
                >
                  <span className="text-sm font-extrabold tracking-wide">Corporate</span>
                  <span className="text-[11px] opacity-75 leading-none mt-0.5">For teams and organizations</span>
                </button>
              </div>
            </div>
          )}

          {/* Email Field */}
          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-semibold text-indigo-300 uppercase tracking-wider px-1">Email Address</label>
            <div className="relative flex items-center">
              <input
                type="email"
                required
                placeholder="example@email.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="w-full bg-[#0a0f1d] border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-3 px-4 text-sm text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium placeholder:text-zinc-600"
              />
            </div>
          </div>

          {/* Username Field (Only on Register) */}
          {!isLogin && (
            <div className="flex flex-col gap-1.5">
              <label className="text-sm font-semibold text-indigo-300 uppercase tracking-wider px-1">Username</label>
              <div className="relative flex items-center">
                <input
                  type="text"
                  required
                  placeholder="username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  className="w-full bg-[#0a0f1d] border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-3 px-4 text-sm text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium placeholder:text-zinc-600"
                />
              </div>
            </div>
          )}

          {/* Company Name Field (Only on Register & Corporate Tier) */}
          {!isLogin && userType === 'corporate' && (
            <div className="flex flex-col gap-1.5 animate-in slide-in-from-top-2 duration-200">
              <label className="text-sm font-semibold text-indigo-300 uppercase tracking-wider px-1">Company Name</label>
              <div className="relative flex items-center">
                <input
                  type="text"
                  required={userType === 'corporate'}
                  placeholder="Company Inc."
                  value={companyName}
                  onChange={(e) => setCompanyName(e.target.value)}
                  className="w-full bg-[#0a0f1d] border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-3 px-4 text-sm text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium placeholder:text-zinc-600"
                />
              </div>
            </div>
          )}

          {/* Password Field */}
          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-semibold text-indigo-300 uppercase tracking-wider px-1">Password</label>
            <div className="relative flex items-center">
              <input
                type="password"
                required
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="w-full bg-[#0a0f1d] border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-3 px-4 text-sm text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium placeholder:text-zinc-600"
              />
            </div>
          </div>

          {/* Submit Button - Solid matching blue-purple colors and copy matching stitch_screen_1/2.png exactly! */}
          <button
            type="submit"
            disabled={isLoading}
            className="w-full mt-5 bg-[#4F46E5] hover:bg-[#4338CA] text-white font-extrabold text-sm tracking-wider uppercase py-3.5 rounded-xl transition-all duration-300 shadow-[0_4px_15px_rgba(79,70,229,0.35)] hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 flex items-center justify-center gap-2 cursor-pointer"
          >
            {isLoading ? (
              <span className="h-5 w-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></span>
            ) : (
              <span>{isLogin ? 'Sign In' : 'Create Account'}</span>
            )}
          </button>
        </form>

        {/* Bottom decorative wave SVG matching stitch_screen_1.png waves! */}
        <div className="absolute bottom-0 left-0 w-full overflow-hidden leading-[0] translate-y-[1px] pointer-events-none rounded-b-3xl">
          <svg className="relative block w-[100%+1.3px] h-[30px]" data-name="Layer 1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 120" preserveAspectRatio="none">
            <path d="M321.39,56.44c58-10.79,114.16-30.13,172-41.86,82.39-16.72,168.19-17.73,250.45-.39C823.78,31,906.67,72,985.66,92.83c70.05,18.48,146.53,26.09,214.34,3V120H0V0C26.9,8.75,55.05,17,83,23.61,167,43.23,252,69.29,321.39,56.44Z" fill="url(#wave-gradient)" opacity="0.15"></path>
            <path d="M985.66,92.83C906.67,72,823.78,31,743.84,14.19c-82.26-17.34-168.06-16.33-250.45.39-57.84,11.73-114,31.07-172,41.86C252,69.29,167,43.23,83,23.61,55.05,17,26.9,8.75,0,0V120H1200V26C1132.19,49.09,1055.71,41.48,985.66,92.83Z" fill="url(#wave-gradient-2)" opacity="0.1"></path>
            <defs>
              <linearGradient id="wave-gradient" x1="0%" y1="0%" x2="100%" y2="0%">
                <stop offset="0%" stopColor="#06b6d4" />
                <stop offset="100%" stopColor="#4f46e5" />
              </linearGradient>
              <linearGradient id="wave-gradient-2" x1="0%" y1="0%" x2="100%" y2="0%">
                <stop offset="0%" stopColor="#4f46e5" />
                <stop offset="100%" stopColor="#d946ef" />
              </linearGradient>
            </defs>
          </svg>
        </div>
      </div>
    </div>
  );
}
