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
      {/* Soft Light Blue Backdrop */}
      <div 
        className="absolute inset-0 bg-sky-950/20 backdrop-blur-sm transition-opacity duration-200"
        onClick={() => setExhaustedModalOpen(false)}
      />

      {/* Sky Glow Shadow Background Element */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[400px] h-[400px] bg-sky-500/10 rounded-full blur-[100px] pointer-events-none" />

      {/* Main Container - Premium Ice Blue Glassmorphism */}
      <div className="relative w-full max-w-md bg-gradient-to-br from-[#f0f9ff] via-[#e0f2fe] to-[#bae6fd] border border-sky-300/50 shadow-[0_20px_50px_rgba(14,165,233,0.18)] rounded-2xl flex flex-col overflow-hidden animate-in zoom-in-95 duration-200 text-sky-950">
        
        {/* Top Accent Line */}
        <div className="absolute top-0 inset-x-0 h-[3px] bg-gradient-to-r from-sky-400 via-sky-500 to-sky-600" />

        {/* Header */}
        <div className="flex justify-between items-center px-6 pt-6 pb-4 border-b border-sky-200/50 bg-white/70">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-xl bg-white border border-sky-200 shadow-[0_2px_8px_rgba(14,165,233,0.08)]">
              <ShieldAlert className="w-5 h-5 text-sky-550" />
            </div>
            <div>
              <h3 className="text-xs font-black text-sky-955 uppercase tracking-widest">
                Credits Exhausted
              </h3>
              <p className="text-[9px] text-sky-600 font-bold tracking-wider uppercase">Daily AI Quota Exceeded</p>
            </div>
          </div>
          <button
            onClick={() => setExhaustedModalOpen(false)}
            className="p-1.5 rounded-lg text-sky-400 hover:text-sky-700 hover:bg-sky-50 transition-all cursor-pointer"
            aria-label="Close modal"
          >
            <X className="w-4.5 h-4.5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-6 space-y-6">
          <p className="text-xs text-sky-900 leading-relaxed font-semibold">
            You have used all <span className="text-sky-600 font-bold">{progressPercent}%</span> of your free daily AI credits for today. Quotas reset daily to maintain server resource stability.
          </p>

          {/* Progress Bar */}
          <div className="space-y-2">
            <div className="flex justify-between text-[9px] font-bold text-sky-700 uppercase tracking-wider">
              <span>Daily Limit Usage</span>
              <span className="text-sky-955">{progressPercent}% Used</span>
            </div>
            <div className="h-2 w-full bg-white border border-sky-200/50 rounded-full overflow-hidden shadow-inner">
              <div 
                className="h-full bg-gradient-to-r from-sky-400 via-sky-500 to-sky-600 rounded-full transition-all duration-500 shadow-[0_0_8px_rgba(14,165,233,0.25)]"
                style={{ width: `${progressPercent}%` }}
              />
            </div>
          </div>

          {/* Reset Info Banner */}
          <div className="bg-white/80 border border-sky-200/60 rounded-xl p-3.5 flex items-center gap-3 text-[11px] text-sky-850 font-medium">
            <HelpCircle className="w-4 h-4 text-sky-500 shrink-0" />
            <span>Your credits will fully reset at <span className="text-sky-955 font-bold">{getResetTimeFormatted()}</span>.</span>
          </div>

          {/* Free Default / Local switch option */}
          <div className="bg-white/85 border border-sky-200/70 rounded-2xl p-4 flex flex-col gap-2.5 text-[11px] text-sky-900 font-medium shadow-sm">
            <div className="flex items-center gap-2 text-sky-800">
              <Sliders className="w-4 h-4 text-sky-550 shrink-0" />
              <span className="font-extrabold uppercase tracking-wide text-[10px]">Switch to Free Local Engine</span>
            </div>
            <p className="text-[10px] text-sky-700 leading-relaxed font-semibold">
              You can instantly bypass credit restrictions by switching your AI configurations to the free <strong className="text-sky-900">Default (Namines)</strong> engine. This routes requests through the local template compiler.
            </p>
            <button
              type="button"
              disabled={isUpdating}
              onClick={handleSwitchToDefault}
              className="flex items-center gap-1.5 px-3 py-1.5 bg-sky-100 hover:bg-sky-200 disabled:opacity-75 text-sky-750 font-black rounded-lg transition-colors cursor-pointer text-[10px] border border-sky-250 self-start"
            >
              {isUpdating ? (
                <>
                  <Loader2 className="w-3 h-3 animate-spin text-sky-600" />
                  <span>Updating configurations...</span>
                </>
              ) : (
                <span>Switch AI Routing to Default (Free)</span>
              )}
            </button>
          </div>

          {/* Action Buttons */}
          <div className="space-y-3 pt-2">
            <button
              onClick={handleOpenSettings}
              className="w-full relative group flex items-center justify-center gap-2 py-3 bg-sky-600 hover:bg-sky-550 text-white font-bold text-xs tracking-wider uppercase rounded-xl shadow-[0_4px_15px_rgba(14,165,233,0.15)] hover:scale-[1.01] active:scale-[0.99] transition-all duration-200 border border-sky-400/20 cursor-pointer"
            >
              <Key className="w-4 h-4 text-sky-200 group-hover:text-white transition-colors" />
              <span>Use My Own API Key (BYOK)</span>
            </button>
            
            <button
              onClick={() => setExhaustedModalOpen(false)}
              className="w-full py-3 bg-white hover:bg-sky-50 text-sky-750 hover:text-sky-900 text-xs font-bold rounded-xl border border-sky-200 hover:border-sky-300 shadow-sm transition-all cursor-pointer text-center"
            >
              I'll wait until tomorrow
            </button>
          </div>
        </div>

        {/* Footer */}
        <div className="px-6 py-3 bg-white/60 border-t border-sky-200/40 flex justify-center items-center text-[9px] text-sky-500/80 font-bold uppercase tracking-widest">
          <span>Darvell Labs</span>
        </div>
      </div>
    </div>
  );
}
