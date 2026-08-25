'use client';

import React, { useState, useRef } from 'react';
import { X, AlertTriangle } from 'lucide-react';
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

const inputClass =
  'w-full bg-surface-700 border border-content-primary/15 focus:border-focus-ring rounded-lg py-2.5 px-3.5 text-sm text-content-primary focus:outline-none transition-all placeholder:text-content-muted';

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

  const handleOAuthPlaceholder = (provider: string) => {
    showToast(`${provider} sign-in is coming soon.`, 'info');
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setErrorMsg(null);

    try {
      if (isLogin) {
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
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-3 sm:p-4 bg-black/70 backdrop-blur-sm animate-fade-in">
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="auth-modal-title"
        className="relative w-full max-w-sm bg-surface-800 border border-content-primary/10 rounded-2xl shadow-[0_20px_60px_rgba(0,0,0,0.6)] overflow-hidden flex flex-col p-5 sm:p-6 max-h-[90vh] overflow-y-auto animate-in zoom-in-95 duration-200"
      >
        {/* Header */}
        <div className="flex items-center justify-between mb-4">
          <h3 id="auth-modal-title" className="text-sm font-bold text-content-primary">
            {isLogin ? 'Sign in' : 'Create account'}
          </h3>
          <button
            onClick={onClose}
            className="p-1 hover:bg-white/[0.06] rounded-md text-content-muted hover:text-content-primary transition-all cursor-pointer"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Tab selector */}
        <div className="flex bg-surface-700 p-1 rounded-lg border border-content-primary/10 mb-4">
          <button
            type="button"
            onClick={() => { setIsLogin(true); setErrorMsg(null); }}
            className={`flex-1 py-1.5 text-center text-xs font-semibold rounded-md transition-all ${
              isLogin ? 'bg-content-primary text-surface-900' : 'bg-transparent text-content-muted hover:text-content-primary'
            }`}
          >
            Login
          </button>
          <button
            type="button"
            onClick={() => { setIsLogin(false); setErrorMsg(null); }}
            className={`flex-1 py-1.5 text-center text-xs font-semibold rounded-md transition-all ${
              !isLogin ? 'bg-content-primary text-surface-900' : 'bg-transparent text-content-muted hover:text-content-primary'
            }`}
          >
            Sign up
          </button>
        </div>

        {/* OAuth — şimdilik UI-only, bkz. FRONTEND.md / backend'de henüz OAuth yok */}
        <div className="flex flex-col gap-2 mb-4">
          <button
            type="button"
            onClick={() => handleOAuthPlaceholder('Google')}
            className="w-full flex items-center justify-center gap-2 py-2.5 rounded-lg border border-content-primary/15 bg-surface-700 hover:bg-surface-600 text-content-primary hover:text-content-primary text-sm font-medium transition-all cursor-pointer"
          >
            <svg className="w-4 h-4" viewBox="0 0 24 24">
              <path fill="var(--color-content-subtle)" d="M21.35 11.1H12v2.9h5.35c-.23 1.4-1.6 4.1-5.35 4.1-3.22 0-5.85-2.66-5.85-5.95S8.78 6.2 12 6.2c1.84 0 3.07.78 3.77 1.45l2.57-2.48C16.7 3.66 14.55 2.7 12 2.7 6.98 2.7 2.9 6.78 2.9 11.8s4.08 9.1 9.1 9.1c5.25 0 8.74-3.69 8.74-8.89 0-.6-.07-1.05-.14-1.51z"/>
            </svg>
            <span>Continue with Google</span>
          </button>
          <button
            type="button"
            onClick={() => handleOAuthPlaceholder('GitHub')}
            className="w-full flex items-center justify-center gap-2 py-2.5 rounded-lg border border-content-primary/15 bg-surface-700 hover:bg-surface-600 text-content-primary hover:text-content-primary text-sm font-medium transition-all cursor-pointer"
          >
            <svg className="w-4 h-4" viewBox="0 0 24 24" fill="var(--color-content-subtle)">
              <path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.5 0-.24-.01-.87-.01-1.71-2.78.62-3.37-1.37-3.37-1.37-.45-1.18-1.11-1.49-1.11-1.49-.91-.64.07-.63.07-.63 1 .07 1.53 1.05 1.53 1.05.9 1.57 2.36 1.12 2.93.86.09-.67.35-1.12.64-1.38-2.22-.26-4.56-1.14-4.56-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.31.1-2.72 0 0 .84-.28 2.75 1.05a9.3 9.3 0 0 1 2.5-.35c.85 0 1.7.12 2.5.35 1.91-1.33 2.75-1.05 2.75-1.05.55 1.41.2 2.46.1 2.72.64.72 1.03 1.63 1.03 2.75 0 3.93-2.34 4.79-4.57 5.05.36.32.68.94.68 1.9 0 1.37-.01 2.47-.01 2.81 0 .28.18.61.69.5A9.99 9.99 0 0 0 22 12.25C22 6.58 17.52 2 12 2z"/>
            </svg>
            <span>Continue with GitHub</span>
          </button>
        </div>

        <div className="flex items-center gap-3 mb-4">
          <div className="h-px flex-1 bg-surface-500" />
          <span className="text-[10px] uppercase tracking-wider text-content-muted">or</span>
          <div className="h-px flex-1 bg-surface-500" />
        </div>

        {errorMsg && (
          <div className="mb-3 px-3 py-2 rounded-lg bg-danger-subtle border border-danger/30 text-danger-text text-xs font-medium flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0 text-danger" />
            <span>{errorMsg}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="flex flex-col gap-2.5">
          {!isLogin && (
            <div className="grid grid-cols-2 gap-2 mb-0.5">
              <button
                type="button"
                onClick={() => setUserType('individual')}
                className={`py-1.5 px-2 text-center border rounded-lg text-xs font-semibold transition-all ${
                  userType === 'individual'
                    ? 'border-focus-ring bg-white/[0.08] text-content-primary'
                    : 'border-content-primary/10 text-content-muted hover:text-content-primary'
                }`}
              >
                Personal
              </button>
              <button
                type="button"
                onClick={() => setUserType('corporate')}
                className={`py-1.5 px-2 text-center border rounded-lg text-xs font-semibold transition-all ${
                  userType === 'corporate'
                    ? 'border-focus-ring bg-white/[0.08] text-content-primary'
                    : 'border-content-primary/10 text-content-muted hover:text-content-primary'
                }`}
              >
                Corporate
              </button>
            </div>
          )}

          <input
            type="email"
            required
            placeholder="Email address"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={inputClass}
          />

          {!isLogin && (
            <input
              type="text"
              required
              placeholder="Username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className={inputClass}
            />
          )}

          {!isLogin && userType === 'corporate' && (
            <input
              type="text"
              required={userType === 'corporate'}
              placeholder="Company name"
              value={companyName}
              onChange={(e) => setCompanyName(e.target.value)}
              className={inputClass}
            />
          )}

          <input
            type="password"
            required
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={inputClass}
          />

          <button
            type="submit"
            disabled={isLoading}
            className="w-full mt-2 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold text-sm py-2.5 rounded-lg transition-all disabled:opacity-50 flex items-center justify-center gap-2 cursor-pointer"
          >
            {isLoading ? (
              <span className="h-4 w-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
            ) : (
              <span>{isLogin ? 'Sign in' : 'Create account'}</span>
            )}
          </button>
        </form>
      </div>
    </div>
  );
}
