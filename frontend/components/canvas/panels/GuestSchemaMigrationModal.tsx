'use client';

import React from 'react';
import { CloudLightning, FolderGit2 } from 'lucide-react';
import { ProjectSnapshot } from '../../../types/project';

interface GuestSchemaMigrationModalProps {
  isOpen: boolean;
  projects: ProjectSnapshot[];
  onSync: () => void;
  onDiscard: () => void;
}

export default function GuestSchemaMigrationModal({
  isOpen,
  projects,
  onSync,
  onDiscard,
}: GuestSchemaMigrationModalProps) {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[10000] flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in animate-none">
      {/* Glow Backdrop Orbs */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[450px] h-[450px] bg-gradient-to-tr from-emerald-500/10 to-indigo-500/10 rounded-full blur-[80px] pointer-events-none -z-10" />

      {/* Main Glassmorphic Container */}
      <div className="relative w-full max-w-md bg-surface-900/95 backdrop-blur-2xl border border-emerald-500/20 rounded-3xl shadow-[0_20px_60px_rgba(0,0,0,0.85)] overflow-hidden flex flex-col p-6 pb-8 animate-in zoom-in-95 duration-200">
        
        {/* Header */}
        <div className="flex items-center justify-between pb-4 border-b border-zinc-800">
          <div className="flex items-center gap-2 text-emerald-400">
            <CloudLightning className="w-5 h-5 shrink-0" />
            <h3 className="text-sm font-extrabold uppercase tracking-wider text-emerald-100">
              Guest Session Migration
            </h3>
          </div>
        </div>

        {/* Title / Description */}
        <div className="my-5 flex flex-col gap-2">
          <span className="text-sm font-bold text-zinc-100 leading-snug">
            You have {projects.length} project{projects.length > 1 ? 's' : ''} from your guest session
          </span>
          <p className="text-[11px] text-zinc-400 leading-relaxed font-semibold">
            Would you like to sync these local projects to your cloud account, or discard them and start fresh?
          </p>
        </div>

        {/* Local Projects List */}
        <div className="max-h-40 overflow-y-auto mb-6 bg-zinc-950/40 border border-zinc-850/80 rounded-2xl p-3 flex flex-col gap-2 custom-scrollbar">
          {projects.map((proj) => {
            const tableCount = proj.schema?.tables?.length || 0;
            const updatedDate = new Date(proj.updatedAt).toLocaleDateString();
            return (
              <div 
                key={proj.id} 
                className="flex items-center justify-between p-2.5 rounded-xl bg-[#121824] border border-zinc-800/50 hover:border-zinc-700/50 transition-all duration-200"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <FolderGit2 className="w-4 h-4 text-indigo-400 shrink-0" />
                  <div className="flex flex-col min-w-0">
                    <span className="text-xs font-bold text-zinc-200 truncate leading-none mb-1">
                      {proj.name}
                    </span>
                    <span className="text-[9px] text-zinc-500 font-semibold leading-none">
                      {proj.dbType} • {tableCount} table{tableCount !== 1 ? 's' : ''}
                    </span>
                  </div>
                </div>
                <span className="text-[9px] text-zinc-500 font-semibold shrink-0">
                  {updatedDate}
                </span>
              </div>
            );
          })}
        </div>

        {/* Action Buttons */}
        <div className="flex gap-3">
          <button
            onClick={onDiscard}
            className="flex-1 py-3 px-4 rounded-xl border border-zinc-700/60 bg-[#121824] hover:bg-zinc-800 text-zinc-300 hover:text-white font-bold text-xs tracking-wider uppercase transition-all duration-200 flex items-center justify-center cursor-pointer"
          >
            <span>Start Fresh</span>
          </button>
          <button
            onClick={onSync}
            className="flex-1 py-3 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white font-extrabold text-xs tracking-wider uppercase transition-all duration-300 shadow-[0_4px_15px_rgba(16,185,129,0.3)] hover:scale-[1.02] active:scale-[0.98] flex items-center justify-center cursor-pointer"
          >
            <span>Sync to Cloud</span>
          </button>
        </div>

      </div>
    </div>
  );
}
