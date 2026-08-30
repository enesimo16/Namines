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
    <div className="fixed inset-0 z-[10000] flex items-center justify-center p-4 bg-scrim/70 backdrop-blur-sm animate-fade-in">
      <div className="relative w-full max-w-sm bg-surface-800 border border-content-primary/12 rounded-[var(--radius-modal)] shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] overflow-hidden flex flex-col p-5 animate-in zoom-in-95 duration-200">

        {/* Header */}
        <div className="flex items-center gap-2 pb-3 border-b border-content-primary/10 text-accent-text">
          <CloudLightning className="w-4 h-4 shrink-0" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-content-primary">
            Guest Session Migration
          </h3>
        </div>

        {/* Title / Description */}
        <div className="my-4 flex flex-col gap-1.5">
          <span className="text-sm font-bold text-content-primary leading-snug">
            You have {projects.length} project{projects.length > 1 ? 's' : ''} from your guest session
          </span>
          <p className="text-xs text-content-muted leading-relaxed">
            Would you like to sync these local projects to your cloud account, or discard them and start fresh?
          </p>
        </div>

        {/* Local Projects List */}
        <div className="max-h-40 overflow-y-auto mb-5 bg-surface-700 border border-content-primary/8 rounded-[var(--radius-card)] p-2 flex flex-col gap-1.5">
          {projects.map((proj) => {
            const tableCount = proj.schema?.tables?.length || 0;
            const updatedDate = new Date(proj.updatedAt).toLocaleDateString();
            return (
              <div
                key={proj.id}
                className="flex items-center justify-between p-2 rounded-[var(--radius-control)] bg-surface-600 border border-content-primary/6"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <FolderGit2 className="w-3.5 h-3.5 text-content-muted shrink-0" />
                  <div className="flex flex-col min-w-0">
                    <span className="text-xs font-semibold text-content-secondary truncate leading-none mb-1">
                      {proj.name}
                    </span>
                    <span className="text-micro text-content-subtle leading-none">
                      {proj.dbType} • {tableCount} table{tableCount !== 1 ? 's' : ''}
                    </span>
                  </div>
                </div>
                <span className="text-micro text-content-subtle shrink-0">
                  {updatedDate}
                </span>
              </div>
            );
          })}
        </div>

        {/* Action Buttons */}
        <div className="flex gap-2">
          <button
            onClick={onDiscard}
            className="flex-1 py-2.5 px-4 rounded-[var(--radius-control)] border border-content-primary/10 bg-surface-700 hover:bg-surface-600 text-content-muted hover:text-content-secondary font-semibold text-xs transition-all cursor-pointer"
          >
            Start Fresh
          </button>
          <button
            onClick={onSync}
            className="flex-1 py-2.5 px-4 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold text-xs transition-all cursor-pointer"
          >
            Sync to Cloud
          </button>
        </div>

      </div>
    </div>
  );
}
