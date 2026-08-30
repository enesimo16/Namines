'use client';

import React from 'react';
import { Sparkles, FileImage, LayoutTemplate, Plus, Database, ChevronRight } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';

export default function EmptyCanvasState() {
  const { schema, addTable, isEditMode, toggleEditMode } = useSchemaStore();
  const showToast = useToastStore((state) => state.showToast);

  // If schema has tables, do not render empty canvas state
  if (!schema || schema.tables.length > 0) return null;

  const handleGenerateWithAi = () => {
    window.dispatchEvent(new CustomEvent('namines:open-regional-prompt'));
  };

  const handleImportFromImage = () => {
    window.dispatchEvent(new CustomEvent('namines:open-vision-modal'));
  };

  const handleBrowseTemplates = () => {
    window.dispatchEvent(new CustomEvent('namines:open-template-gallery'));
  };

  const handleStartFromScratch = () => {
    // Add table in the center area of flow view
    addTable(450, 200);
    if (!isEditMode) {
      toggleEditMode();
    }
    showToast('Empty table initialized. Edit mode activated.', 'success');
  };

  return (
    <div className="absolute inset-0 z-[40] flex items-center justify-center bg-scrim/75 backdrop-blur-sm pointer-events-auto">
      <div className="relative w-full max-w-md p-8 rounded-[var(--radius-modal)] bg-surface-900/95 border border-surface-600 shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 80%, transparent)] backdrop-blur-2xl flex flex-col items-center text-center gap-6 animate-in zoom-in-95 duration-200 font-sans">
        
        {/* Header Icon */}
        <div className="w-12 h-12 rounded-[var(--radius-card)] bg-content-primary/[0.06] border border-content-primary/12 text-accent-text flex items-center justify-center shadow-sm shrink-0">
          <Database className="w-5 h-5" />
        </div>

        {/* Title and Subtitle */}
        <div className="space-y-1.5">
          <h2 className="text-sm font-extrabold text-content-primary uppercase tracking-wider">
            Create Your Database Schema
          </h2>
          <p className="text-xs text-content-muted leading-relaxed max-w-xs">
            Your canvas is currently empty. Initialize your project using AI, importing an existing visual sketch, or by building the design manually.
          </p>
        </div>

        {/* Divider */}
        <div className="w-full h-px bg-surface-700/80" />

        {/* Actions List */}
        <div className="flex flex-col gap-3 w-full">
          {/* Action 1: Generate with AI */}
          <button
            onClick={handleGenerateWithAi}
            className="group relative flex items-center justify-between p-4 rounded-[var(--radius-card)] bg-surface-900/40 hover:bg-surface-900/85 border border-surface-600/80 hover:border-surface-500/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-content-primary/[0.06] border border-content-primary/12 flex items-center justify-center text-accent-text">
                <Sparkles className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-content-primary">Generate with AI</h4>
                <p className="text-[10px] text-content-subtle font-medium mt-0.5">Describe your database structure in plain Turkish or English.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-subtle group-hover:text-content-muted group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 2: Import from Image */}
          <button
            onClick={handleImportFromImage}
            className="group relative flex items-center justify-between p-4 rounded-[var(--radius-card)] bg-surface-900/40 hover:bg-surface-900/85 border border-surface-600/80 hover:border-surface-500/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-accent/10 border border-accent/20 flex items-center justify-center text-accent-text">
                <FileImage className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-content-primary">Import from Image</h4>
                <p className="text-[10px] text-content-subtle font-medium mt-0.5">Extract schema tables and fields from a sketch or diagram photo.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-subtle group-hover:text-content-muted group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 3: Browse Templates */}
          <button
            onClick={handleBrowseTemplates}
            className="group relative flex items-center justify-between p-4 rounded-[var(--radius-card)] bg-surface-900/40 hover:bg-surface-900/85 border border-surface-600/80 hover:border-surface-500/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-content-primary/[0.04] border border-accent-hover/20 flex items-center justify-center text-content-muted">
                <LayoutTemplate className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-content-primary">Browse Templates</h4>
                <p className="text-[10px] text-content-subtle font-medium mt-0.5">Pick a pre-built schema: e-commerce, SaaS, CRM, healthcare, and more.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-subtle group-hover:text-content-muted group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 4: Start from Scratch */}
          <button
            onClick={handleStartFromScratch}
            className="group relative flex items-center justify-between p-4 rounded-[var(--radius-card)] bg-surface-900/40 hover:bg-surface-900/85 border border-surface-600/80 hover:border-surface-500/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-success/10 border border-success/20 flex items-center justify-center text-success-text">
                <Plus className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-content-primary">Start from Scratch</h4>
                <p className="text-[10px] text-content-subtle font-medium mt-0.5">Manually place tables and customize fields on a clean canvas.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-subtle group-hover:text-content-muted group-hover:translate-x-0.5 transition-all" />
          </button>
        </div>

      </div>
    </div>
  );
}
