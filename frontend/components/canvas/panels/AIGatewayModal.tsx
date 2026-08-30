'use client';

import React, { useState } from 'react';
import { X, Lock, Sparkles, Key, LogIn, CheckCircle, ShieldAlert } from 'lucide-react';
import { useAIGatewayStore } from '../../../store/useAIGatewayStore';
import { useByokStore } from '../../../store/useByokStore';
import { useToastStore } from '../../../store/useToastStore';

export default function AIGatewayModal() {
  const { isOpen, featureName, closeGateway } = useAIGatewayStore();
  const { apiKey, provider, setApiKey, setProvider, clearApiKey } = useByokStore();
  const showToast = useToastStore(state => state.showToast);

  const [inputKey, setInputKey] = useState(apiKey || '');
  const [selectedProvider, setSelectedProvider] = useState(provider);
  const [isSaved, setIsSaved] = useState(!!apiKey);

  if (!isOpen) return null;

  const handleSaveKey = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputKey.trim()) {
      clearApiKey();
      setIsSaved(false);
      showToast('API Key removed. Enterprise fallback will be used if authenticated.', 'info');
      return;
    }
    setApiKey(inputKey.trim());
    setProvider(selectedProvider);
    setIsSaved(true);
    showToast('API Key obfuscated and saved securely.', 'success');
  };

  const handleClearKey = () => {
    clearApiKey();
    setInputKey('');
    setIsSaved(false);
    showToast('API Key deleted from secure local storage.', 'info');
  };

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4 bg-scrim/75 backdrop-blur-sm animate-fade-in">
      {/* Click outside to close */}
      <div className="absolute inset-0" onClick={closeGateway} />

      {/* Main Container - Minimalist Dark Glass Theme */}
      <div className="relative w-full max-w-md bg-surface-900/95 border border-surface-600 shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 80%, transparent)] rounded-3xl backdrop-blur-2xl flex flex-col overflow-hidden animate-in zoom-in-95 duration-200 font-sans">
        
        {/* Modal Header */}
        <div className="flex justify-between items-center px-6 py-4.5 border-b border-surface-600/80 bg-surface-900/20">
          <div className="flex items-center gap-2.5">
            <Lock className="w-4.5 h-4.5 text-accent-text" />
            <div>
              <h3 className="text-xs font-extrabold text-content-primary uppercase tracking-wider">
                AI Authentication Required
              </h3>
              <p className="text-[9px] text-content-subtle font-mono tracking-wider uppercase">Secure AI Gateway</p>
            </div>
          </div>
          <button
            onClick={closeGateway}
            className="p-1 hover:bg-white/5 rounded-lg text-content-muted hover:text-content-primary transition-all cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Modal Body */}
        <div className="p-6 space-y-6 flex-1 overflow-y-auto">
          {/* Warning Banner */}
          <div className="bg-content-primary/[0.04] border border-accent-hover/15 rounded-xl p-4 flex gap-3 relative overflow-hidden">
            <ShieldAlert className="w-5 h-5 text-accent-text shrink-0 mt-0.5" />
            <div className="space-y-1">
              <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">AI Operations Restricted</h4>
              <p className="text-[11px] text-content-muted leading-relaxed font-semibold">
                You are currently accessing the canvas as a <strong className="text-accent-text">Guest</strong>. The feature <span className="text-content-secondary font-mono font-bold">"{featureName || 'AI Agent'}"</span> requires enterprise credits or your own secure API token to prevent resource drainage.
              </p>
            </div>
          </div>

          {/* Option A: Login / Sign Up */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-[9px] font-extrabold text-content-subtle uppercase tracking-widest font-mono">
                Option I: Sign In to Account
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
              className="w-full group relative flex items-center justify-center gap-2 py-2.5 bg-content-primary hover:bg-content-secondary text-surface-900 font-bold text-xs uppercase tracking-wider rounded-xl transition-all duration-200 cursor-pointer shadow-md"
            >
              <LogIn className="w-4 h-4 text-white/95 group-hover:translate-x-0.5 transition-transform" />
              <span>Access Namines Cloud</span>
            </button>
          </div>

          {/* Option B: BYOK Interface */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-[9px] font-extrabold text-content-subtle uppercase tracking-widest font-mono">
                Option II: Secure BYOK (Own API Key)
              </span>
              <div className="h-px flex-1 bg-surface-700/60 ml-3" />
            </div>

            <form onSubmit={handleSaveKey} className="bg-surface-900/45 border border-surface-600/80 p-5 rounded-2xl space-y-4">
              {/* Tab Selector */}
              <div className="flex bg-surface-900/80 p-1 rounded-xl border border-surface-600/60">
                {(['groq', 'openai', 'anthropic', 'gemini'] as const).map((prov) => (
                  <button
                    key={prov}
                    type="button"
                    onClick={() => {
                      if (!isSaved) setSelectedProvider(prov);
                    }}
                    disabled={isSaved}
                    className={`flex-1 py-1.5 text-[10px] font-extrabold uppercase tracking-wider rounded-lg transition-all duration-200 cursor-pointer ${
                      selectedProvider === prov
                        ? 'bg-surface-700 border-surface-500 text-content-primary shadow-sm'
                        : 'bg-transparent border-transparent text-content-subtle hover:text-content-secondary'
                    }`}
                  >
                    {prov}
                  </button>
                ))}
              </div>

              <div className="space-y-1.5">
                <label className="text-[10px] font-extrabold text-content-muted uppercase tracking-wider block">
                  API Key Input
                </label>
                <div className="relative">
                  <input
                    type="password"
                    value={inputKey}
                    onChange={(e) => setInputKey(e.target.value)}
                    disabled={isSaved}
                    placeholder={isSaved ? "••••••••••••••••••••" : `Enter your ${selectedProvider.toUpperCase()} Key`}
                    className="w-full px-3 py-2 bg-surface-900 border border-surface-600 rounded-xl text-xs text-content-primary placeholder-zinc-700 focus:outline-none focus:border-accent-hover transition-all font-mono"
                  />
                  <div className="absolute right-3 top-1/2 -translate-y-1/2 text-content-subtle">
                    <Key className="w-3.5 h-3.5" />
                  </div>
                </div>
                <span className="text-[9px] text-content-subtle block mt-1 font-sans leading-normal">
                  * Your key is encrypted in local storage with an AES-based obfuscation layer against XSS attacks.
                </span>
              </div>

              {isSaved ? (
                <div className="flex gap-2">
                  <div className="flex-1 py-2 px-3 bg-success-subtle/20 border border-success/20 rounded-xl flex items-center gap-2 text-success-text text-xs font-bold font-mono">
                    <CheckCircle className="w-3.5 h-3.5 shrink-0" />
                    <span>Securely Saved</span>
                  </div>
                  <button
                    type="button"
                    onClick={handleClearKey}
                    className="px-4 bg-surface-700 hover:bg-surface-600 border border-surface-500 text-content-secondary hover:text-content-primary text-xs font-bold rounded-xl transition-all cursor-pointer"
                  >
                    Remove
                  </button>
                </div>
              ) : (
                <button
                  type="submit"
                  className="w-full py-2.5 bg-content-primary hover:bg-content-secondary text-surface-900 font-bold text-xs tracking-wider uppercase rounded-xl transition-all duration-200 cursor-pointer shadow-sm"
                >
                  Encrypt and Mount Key
                </button>
              )}
            </form>
          </div>
        </div>

        {/* Modal Footer */}
        <div className="px-6 py-4 bg-surface-900/40 border-t border-surface-600/80 flex justify-center items-center text-[9px] text-content-subtle font-mono tracking-widest select-none">
          <span>DARVELL LABS</span>
        </div>
      </div>
    </div>
  );
}
