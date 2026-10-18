'use client';

import React from 'react';
import { useRouter } from 'next/navigation';
import { Sparkles, FileImage, LayoutTemplate, Plus, Database, ChevronRight } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';

export default function EmptyCanvasState() {
  const router = useRouter();
  const { schema, addTable, isEditMode, toggleEditMode } = useSchemaStore();
  const showToast = useToastStore((state) => state.showToast);

  // If schema has tables, do not render empty canvas state
  if (!schema || schema.tables.length > 0) return null;

  const handleGenerateWithAi = () => {
    router.push('/new');
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
    <div className="absolute inset-0 z-modal-backdrop flex items-center justify-center bg-scrim/75 backdrop-blur-sm pointer-events-auto">
      <div className="relative w-full max-w-md p-7 rounded-[var(--radius-modal)] bg-surface-800/95 border border-line-strong shadow-2xl backdrop-blur-2xl flex flex-col items-center text-center gap-5 animate-in zoom-in-95 duration-200 font-sans">
        
        {/* Header Icon */}
        <div className="w-12 h-12 rounded-[var(--radius-card)] bg-surface-700/80 border border-line text-content-primary flex items-center justify-center shadow-xs shrink-0">
          <Database className="w-5 h-5" />
        </div>

        {/* Title and Subtitle */}
        <div className="space-y-1">
          <h2 className="text-base font-bold text-content-primary tracking-tight">
            Create Your Database Schema
          </h2>
          <p className="text-xs text-content-muted leading-relaxed max-w-xs">
            Your canvas is currently empty. Initialize your schema with AI, import a sketch photo, or start building manually.
          </p>
        </div>

        {/* Divider */}
        <div className="w-full h-px bg-line" />

        {/* Actions List */}
        <div className="flex flex-col gap-2.5 w-full">
          {/* Action 1: Generate with AI */}
          <button
            onClick={handleGenerateWithAi}
            className="group relative flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-700/30 hover:bg-surface-700/70 border border-line hover:border-line-strong transition-all text-left cursor-pointer shadow-xs"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 border border-line flex items-center justify-center text-content-primary shrink-0">
                <Sparkles className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-semibold text-content-primary">Generate with AI</h3>
                <p className="text-[10px] text-content-muted mt-0.5">Describe your database structure in plain English.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-muted group-hover:text-content-primary group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 2: Import from Image */}
          <button
            onClick={handleImportFromImage}
            className="group relative flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-700/30 hover:bg-surface-700/70 border border-line hover:border-line-strong transition-all text-left cursor-pointer shadow-xs"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 border border-line flex items-center justify-center text-content-primary shrink-0">
                <FileImage className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-semibold text-content-primary">Import from Image</h3>
                <p className="text-[10px] text-content-muted mt-0.5">Extract tables and fields from a whiteboard or diagram photo.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-muted group-hover:text-content-primary group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 3: Browse Templates */}
          <button
            onClick={handleBrowseTemplates}
            className="group relative flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-700/30 hover:bg-surface-700/70 border border-line hover:border-line-strong transition-all text-left cursor-pointer shadow-xs"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 border border-line flex items-center justify-center text-content-primary shrink-0">
                <LayoutTemplate className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-semibold text-content-primary">Browse Templates</h3>
                <p className="text-[10px] text-content-muted mt-0.5">Pick a pre-built schema: e-commerce, SaaS, CRM, and more.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-muted group-hover:text-content-primary group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 4: Start from Scratch */}
          <button
            onClick={handleStartFromScratch}
            className="group relative flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-700/30 hover:bg-surface-700/70 border border-line hover:border-line-strong transition-all text-left cursor-pointer shadow-xs"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 border border-line flex items-center justify-center text-content-primary shrink-0">
                <Plus className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-semibold text-content-primary">Start from Scratch</h3>
                <p className="text-[10px] text-content-muted mt-0.5">Manually place tables and customize fields on a clean canvas.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-content-muted group-hover:text-content-primary group-hover:translate-x-0.5 transition-all" />
          </button>
        </div>

      </div>
    </div>
  );
}
