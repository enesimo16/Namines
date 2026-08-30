import React, { useState } from 'react';
import { X, ShieldAlert, Key, HelpCircle, Sliders, Loader2 } from 'lucide-react';
import { useQuotaStore } from '../../../store/useQuotaStore';
import { useAIPolicyStore } from '../../../store/useAIPolicyStore';
import { useToastStore } from '../../../store/useToastStore';

export default function QuotaExhaustedModal() {
  const { isExhaustedModalOpen, setExhaustedModalOpen, dailyLimit, used, resetAt } = useQuotaStore();
  const { updatePolicy } = useAIPolicyStore();
  const showToast = useToastStore(state => state.showToast);
  const [isUpdating, setIsUpdating] = useState(false);

  if (!isExhaustedModalOpen) return null;

  const handleOpenSettings = () => {
    setExhaustedModalOpen(false);
    window.dispatchEvent(new CustomEvent('namines:open-ai-settings'));
  };

  const handleSwitchToDefault = async () => {
    setIsUpdating(true);
    try {
      await updatePolicy({
        smartSeed: 0,
        documentation: 0,
        scaffolding: 0,
        schemaGeneration: 0,
        schemaRevision: 0,
        dbaAnalysis: 0,
        migration: 0,
        voice: 0
      });
      showToast('All AI configurations successfully switched to Default (Local)!', 'success');
      setExhaustedModalOpen(false);
    } catch {
      showToast('Failed to update routing policies. Please try again from settings.', 'error');
    } finally {
      setIsUpdating(false);
    }
  };

  const getResetTimeFormatted = () => {
    if (!resetAt) return 'midnight UTC';
    try {
      const date = new Date(resetAt);
      return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ' ' + date.toLocaleDateString([], { month: 'short', day: 'numeric' });
    } catch {
      return 'midnight UTC';
    }
  };

  const progressPercent = Math.min(100, Math.round((used / dailyLimit) * 100));

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-scrim/70 backdrop-blur-sm"
        onClick={() => setExhaustedModalOpen(false)}
      />

      <div className="relative w-full max-w-sm bg-surface-800 border border-content-primary/12 shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] rounded-2xl flex flex-col overflow-hidden animate-in zoom-in-95 duration-200">

        {/* Header */}
        <div className="flex justify-between items-center px-5 pt-5 pb-3 border-b border-content-primary/10">
          <div className="flex items-center gap-2.5">
            <div className="p-1.5 rounded-lg bg-surface-600 border border-content-primary/15">
              <ShieldAlert className="w-4 h-4 text-content-secondary" />
            </div>
            <div>
              <h3 className="text-xs font-bold text-content-primary uppercase tracking-wider">
                Credits Exhausted
              </h3>
              <p className="text-[10px] text-content-subtle">Daily AI quota exceeded</p>
            </div>
          </div>
          <button
            onClick={() => setExhaustedModalOpen(false)}
            className="p-1 rounded-lg text-content-subtle hover:text-content-primary hover:bg-white/[0.06] transition-all cursor-pointer"
            aria-label="Close modal"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Body */}
        <div className="p-5 space-y-4">
          <p className="text-xs text-content-primary leading-relaxed">
            You have used <span className="text-content-primary font-semibold">{progressPercent}%</span> of your free daily AI credits. Quotas reset daily to keep server resources stable.
          </p>

          {/* Progress Bar */}
          <div className="space-y-1.5">
            <div className="flex justify-between text-[10px] font-semibold text-content-subtle uppercase tracking-wider">
              <span>Daily Limit Usage</span>
              <span className="text-content-primary">{progressPercent}%</span>
            </div>
            <div className="h-1.5 w-full bg-surface-700 rounded-full overflow-hidden">
              <div
                className="h-full bg-focus-ring rounded-full transition-all duration-500"
                style={{ width: `${progressPercent}%` }}
              />
            </div>
          </div>

          {/* Reset Info */}
          <div className="bg-surface-700 border border-content-primary/8 rounded-xl p-3 flex items-center gap-2.5 text-[11px] text-content-primary">
            <HelpCircle className="w-3.5 h-3.5 text-content-muted shrink-0" />
            <span>Credits reset at <span className="text-content-primary font-semibold">{getResetTimeFormatted()}</span>.</span>
          </div>

          {/* Free Default / Local switch option */}
          <div className="bg-surface-700 border border-content-primary/8 rounded-xl p-3.5 flex flex-col gap-2 text-[11px] text-content-primary">
            <div className="flex items-center gap-2 text-content-primary">
              <Sliders className="w-3.5 h-3.5 text-content-muted shrink-0" />
              <span className="font-bold uppercase tracking-wide text-[10px]">Switch to Free Local Engine</span>
            </div>
            <p className="text-[10px] text-content-muted leading-relaxed">
              Instantly bypass credit restrictions by switching your AI configurations to the free <strong className="text-content-primary">Default (Namines)</strong> engine — routes requests through the local template compiler.
            </p>
            <button
              type="button"
              disabled={isUpdating}
              onClick={handleSwitchToDefault}
              className="flex items-center gap-1.5 px-3 py-1.5 bg-white/[0.08] hover:bg-white/[0.12] disabled:opacity-60 text-content-primary font-semibold rounded-lg transition-colors cursor-pointer text-[10px] border border-white/15 self-start"
            >
              {isUpdating ? (
                <>
                  <Loader2 className="w-3 h-3 animate-spin" />
                  <span>Updating configurations...</span>
                </>
              ) : (
                <span>Switch AI Routing to Default (Free)</span>
              )}
            </button>
          </div>

          {/* Action Buttons */}
          <div className="space-y-2 pt-1">
            <button
              onClick={handleOpenSettings}
              className="w-full flex items-center justify-center gap-2 py-2.5 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold text-xs rounded-xl transition-all cursor-pointer"
            >
              <Key className="w-3.5 h-3.5" />
              <span>Use My Own API Key (BYOK)</span>
            </button>

            <button
              onClick={() => setExhaustedModalOpen(false)}
              className="w-full py-2.5 bg-transparent hover:bg-white/[0.04] text-content-muted hover:text-content-primary text-xs font-medium rounded-xl border border-content-primary/10 transition-all cursor-pointer text-center"
            >
              I'll wait until tomorrow
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
