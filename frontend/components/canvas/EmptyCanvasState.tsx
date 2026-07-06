'use client';

import React from 'react';
import { Sparkles, FileImage, Plus, Database, ChevronRight } from 'lucide-react';
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

  const handleStartFromScratch = () => {
    // Add table in the center area of flow view
    addTable(450, 200);
    if (!isEditMode) {
      toggleEditMode();
    }
    showToast('Empty table initialized. Edit mode activated.', 'success');
  };

  return (
    <div className="absolute inset-0 z-[40] flex items-center justify-center bg-black/75 backdrop-blur-sm pointer-events-auto">
      <div className="relative w-full max-w-md p-8 rounded-3xl bg-[#09111F]/95 border border-zinc-800 shadow-[0_20px_60px_rgba(0,0,0,0.8)] backdrop-blur-2xl flex flex-col items-center text-center gap-6 animate-in zoom-in-95 duration-200 font-sans">
        
        {/* Header Icon */}
        <div className="w-12 h-12 rounded-xl bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 flex items-center justify-center shadow-sm shrink-0">
          <Database className="w-5 h-5" />
        </div>

        {/* Title and Subtitle */}
        <div className="space-y-1.5">
          <h2 className="text-sm font-extrabold text-zinc-100 uppercase tracking-wider">
            Create Your Database Schema
          </h2>
          <p className="text-xs text-zinc-400 leading-relaxed max-w-xs">
            Your canvas is currently empty. Initialize your project using AI, importing an existing visual sketch, or by building the design manually.
          </p>
        </div>

        {/* Divider */}
        <div className="w-full h-px bg-zinc-800/80" />

        {/* Actions List */}
        <div className="flex flex-col gap-3 w-full">
          {/* Action 1: Generate with AI */}
          <button
            onClick={handleGenerateWithAi}
            className="group relative flex items-center justify-between p-4 rounded-xl bg-zinc-950/40 hover:bg-zinc-950/85 border border-zinc-800/80 hover:border-zinc-700/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-lg bg-indigo-500/10 border border-indigo-500/20 flex items-center justify-center text-indigo-400">
                <Sparkles className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-zinc-200">Generate with AI</h4>
                <p className="text-[10px] text-zinc-500 font-medium mt-0.5">Describe your database structure in plain Turkish or English.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-zinc-600 group-hover:text-zinc-400 group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 2: Import from Image */}
          <button
            onClick={handleImportFromImage}
            className="group relative flex items-center justify-between p-4 rounded-xl bg-zinc-950/40 hover:bg-zinc-950/85 border border-zinc-800/80 hover:border-zinc-700/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-lg bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-sky-400">
                <FileImage className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-zinc-200">Import from Image</h4>
                <p className="text-[10px] text-zinc-500 font-medium mt-0.5">Extract schema tables and fields from a sketch or diagram photo.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-zinc-600 group-hover:text-zinc-400 group-hover:translate-x-0.5 transition-all" />
          </button>

          {/* Action 3: Start from Scratch */}
          <button
            onClick={handleStartFromScratch}
            className="group relative flex items-center justify-between p-4 rounded-xl bg-zinc-950/40 hover:bg-zinc-950/85 border border-zinc-800/80 hover:border-zinc-700/85 transition-all text-left cursor-pointer"
          >
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
                <Plus className="w-4 h-4" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-zinc-200">Start from Scratch</h4>
                <p className="text-[10px] text-zinc-500 font-medium mt-0.5">Manually place tables and customize fields on a clean canvas.</p>
              </div>
            </div>
            <ChevronRight className="w-4 h-4 text-zinc-600 group-hover:text-zinc-400 group-hover:translate-x-0.5 transition-all" />
          </button>
        </div>

      </div>
    </div>
  );
}
