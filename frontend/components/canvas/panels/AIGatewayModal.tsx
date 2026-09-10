'use client';

import React from 'react';
import { X, Lock, LogIn, ShieldAlert } from 'lucide-react';
import { useAIGatewayStore } from '../../../store/useAIGatewayStore';
import { useToastStore } from '../../../store/useToastStore';

export default function AIGatewayModal() {
  const { isOpen, featureName, closeGateway } = useAIGatewayStore();
  const showToast = useToastStore(state => state.showToast);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4 bg-scrim/75 backdrop-blur-sm animate-fade-in">
      {/* Click outside to close */}
      <div className="absolute inset-0" onClick={closeGateway} />

      {/* Main Container - Minimalist Dark Glass Theme */}
      <div className="relative w-full max-w-md bg-surface-900/95 border border-surface-600 shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 80%, transparent)] rounded-[var(--radius-modal)] backdrop-blur-2xl flex flex-col overflow-hidden animate-in zoom-in-95 duration-200 font-sans">

        {/* Modal Header */}
        <div className="flex justify-between items-center px-6 py-4.5 border-b border-surface-600/80 bg-surface-900/20">
          <div className="flex items-center gap-2.5">
            <Lock className="w-4.5 h-4.5 text-accent-text" />
            <div>
              <h3 className="text-xs font-extrabold text-content-primary uppercase tracking-wider">
                AI Authentication Required
              </h3>
              <p className="text-micro text-content-subtle font-mono tracking-wider uppercase">Secure AI Gateway</p>
            </div>
          </div>
          <button
            onClick={closeGateway}
            className="p-1 hover:bg-white/5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary transition-all cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Modal Body */}
        <div className="p-6 space-y-6 flex-1 overflow-y-auto">
          {/* Warning Banner */}
          <div className="bg-content-primary/[0.04] border border-accent-hover/15 rounded-[var(--radius-card)] p-4 flex gap-3 relative overflow-hidden">
            <ShieldAlert className="w-5 h-5 text-accent-text shrink-0 mt-0.5" />
            <div className="space-y-1">
              <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">AI Operations Restricted</h4>
              <p className="text-[11px] text-content-muted leading-relaxed font-semibold">
                You are currently accessing the canvas as a <strong className="text-accent-text">Guest</strong>. The feature <span className="text-content-secondary font-mono font-bold">&quot;{featureName || 'AI Agent'}&quot;</span> requires an authenticated account.
              </p>
            </div>
          </div>

          {/* Sign In / Sign Up */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-micro font-extrabold text-content-subtle uppercase tracking-widest font-mono">
                Sign In to Continue
              </span>
              <div className="h-px flex-1 bg-surface-700/60 ml-3" />
            </div>

            <button
              onClick={() => {
                closeGateway();
                const authBtn = document.getElementById('auth-modal-trigger');
                if (authBtn) {
                  authBtn.click();
                } else {
                  showToast('Please sign in or create an account via the Header navigation.', 'info');
                }
              }}
              className="w-full group relative flex items-center justify-center gap-2 py-2.5 bg-content-primary hover:bg-content-secondary text-surface-900 font-bold text-xs uppercase tracking-wider rounded-[var(--radius-card)] transition-all duration-200 cursor-pointer shadow-md"
            >
              <LogIn className="w-4 h-4 text-white/95 group-hover:translate-x-0.5 transition-transform" />
              <span>Access Namines Cloud</span>
            </button>
          </div>
        </div>

        {/* Modal Footer */}
        <div className="px-6 py-4 bg-surface-900/40 border-t border-surface-600/80 flex justify-center items-center text-micro text-content-subtle font-mono tracking-widest select-none">
          <span>DARVELL LABS</span>
        </div>
      </div>
    </div>
  );
}
