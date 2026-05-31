import React from 'react';
import { GitMerge, X, ArrowRight, Check, CheckCircle2, ChevronRight, AlertTriangle } from 'lucide-react';
import { useBranchStore } from '../../../store/useBranchStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { DatabaseSchema, SchemaTable, SchemaColumn } from '../../../types/schema';

export default function ConflictResolverModal() {
  const { 
    isConflictModalOpen, 
    mergeSourceBranch, 
    mergeTargetBranch, 
    conflicts, 
    updateConflictChoice, 
    resetMergeSession,
    setIsDiffMode
  } = useBranchStore();

  const { projects, activeProjectId, mergeBranch } = useProjectHistoryStore();
  const { schema, loadFromSchema } = useSchemaStore();

  const showToast = useToastStore(state => state.showToast);

  if (!isConflictModalOpen || !schema || !activeProjectId) return null;

  const activeProject = projects.find(p => p.id === activeProjectId);
  if (!activeProject) return null;

  // Auto-resolve helper
  const handleAutoResolve = (choice: 'source' | 'target') => {
    conflicts.forEach(c => {
      updateConflictChoice(c.id, choice);
    });
  };

  // Compile resolutions and perform Git Merge
  const handleCompleteMerge = () => {
    // Clone active branch schema as a baseline
    const mergedSchema: DatabaseSchema = JSON.parse(JSON.stringify(schema));
    
    // Fetch source branch (the compared branch we are pulling changes from)
    const sourceBranch = activeProject.branches?.find(b => b.name === mergeSourceBranch);
    if (!sourceBranch) return;

    // Apply choices
    conflicts.forEach(item => {
      // Source value is what exists in Active Branch. Target value is what exists in Incoming Branch.
      // If user chooses 'source', we keep what we have (no change needed in mergedSchema since it's a clone of active).
      // If user chooses 'target', we apply the incoming branch's structure!
      if (item.selectedChoice === 'target') {
        if (item.type === 'table_added') {
          // 'target' is null (didn't exist in compare branch), so we delete the table
          mergedSchema.tables = mergedSchema.tables.filter(t => t.id !== item.id);
        } else if (item.type === 'table_deleted') {
          // 'target' is the table (existed in compare branch but missing here), so we restore it
          if (item.targetValue) {
            mergedSchema.tables.push(item.targetValue);
          }
        } else if (item.type === 'table_name') {
          // Update table name to target value (compare branch's table name)
          const targetTable = mergedSchema.tables.find(t => t.name === item.tableName);
          if (targetTable) {
            targetTable.name = item.targetValue;
          }
        } else if (item.type === 'column_added') {
          // Target is null (didn't exist in compare), so we remove this column
          const parts = item.id.split('-'); // tableId-colId-added
          const tableId = parts[0];
          const colId = parts[1];
          const targetTable = mergedSchema.tables.find(t => t.id === tableId);
          if (targetTable) {
            targetTable.columns = targetTable.columns.filter(c => c.id !== colId);
          }
        } else if (item.type === 'column_deleted') {
          // Target is the column (existed in compare, missing here), so we restore it
          const parts = item.id.split('-'); // tableId-colId-deleted
          const tableId = parts[0];
          if (item.targetValue) {
            const targetTable = mergedSchema.tables.find(t => t.id === tableId);
            if (targetTable) {
              targetTable.columns.push(item.targetValue);
            }
          }
        } else if (item.type === 'column_modified') {
          // Target is the modified column structure, so we overwrite it
          const parts = item.id.split('-'); // tableId-colId-modified
          const tableId = parts[0];
          const colId = parts[1];
          const targetTable = mergedSchema.tables.find(t => t.id === tableId);
          if (targetTable && item.targetValue) {
            targetTable.columns = targetTable.columns.map(c => 
              c.id === colId ? item.targetValue : c
            );
          }
        }
      }
    });

    // Save final merged structure in the target branch (which is our active branch!)
    if (mergeTargetBranch) {
      mergeBranch(
        activeProject.id, 
        mergeSourceBranch || '', 
        mergeTargetBranch, 
        mergedSchema, 
        activeProject.nodePositions
      );
      
      // Load back into schema store
      loadFromSchema(mergedSchema, activeProject.nodePositions);
    }

    // Close merge session and disable diff mode
    setIsDiffMode(false);
    resetMergeSession();
    showToast("Branches merged successfully!", "success");
  };

  const getConflictBadge = (type: string) => {
    switch(type) {
      case 'table_added': return <span className="bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 px-2 py-0.5 rounded text-[10px] font-bold">New Table</span>;
      case 'table_deleted': return <span className="bg-rose-500/20 text-rose-400 border border-rose-500/30 px-2 py-0.5 rounded text-[10px] font-bold">Deleted Table</span>;
      case 'table_name': return <span className="bg-amber-500/20 text-amber-400 border border-amber-500/30 px-2 py-0.5 rounded text-[10px] font-bold">Name Change</span>;
      case 'column_added': return <span className="bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 px-2 py-0.5 rounded text-[10px]">New Column</span>;
      case 'column_deleted': return <span className="bg-rose-500/10 text-rose-400 border border-rose-500/20 px-2 py-0.5 rounded text-[10px]">Deleted Column</span>;
      case 'column_modified': return <span className="bg-amber-500/10 text-amber-400 border border-amber-500/20 px-2 py-0.5 rounded text-[10px]">Column Modification</span>;
      default: return null;
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-md animate-in fade-in duration-300">
      <div className="bg-[#0F172A] border border-indigo-500/30 rounded-3xl w-[90vw] max-w-5xl h-[85vh] flex flex-col shadow-[0_0_50px_rgba(99,102,241,0.25)] overflow-hidden">
        
        {/* Header */}
        <div className="bg-slate-900/90 border-b border-indigo-500/20 px-6 py-4 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="h-10 w-10 bg-indigo-500/10 border border-indigo-500/25 rounded-2xl flex items-center justify-center">
              <GitMerge className="w-5 h-5 text-indigo-400 animate-pulse" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-zinc-100 flex items-center gap-2 animate-none">
                Merge Branches (Git Merge)
              </h2>
              <p className="text-xs text-indigo-300/80">
                Changes from <span className="font-semibold text-indigo-200">{mergeSourceBranch}</span> are being merged into your active branch <span className="font-semibold text-indigo-200">{mergeTargetBranch}</span>.
              </p>
            </div>
          </div>
          
          <button 
            onClick={resetMergeSession}
            className="p-2 hover:bg-zinc-800 rounded-xl text-zinc-400 hover:text-white transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Warning banner */}
        <div className="bg-indigo-500/10 border-b border-indigo-500/20 px-6 py-3.5 flex items-center justify-between text-xs text-indigo-200">
          <div className="flex items-center gap-2 font-medium">
            <AlertTriangle className="w-4 h-4 text-indigo-400 animate-none" />
            <span>A total of <strong>{conflicts.length}</strong> structural changes detected. Please select the branch version you want to apply for each row.</span>
          </div>
          <div className="flex gap-2">
            <button
              onClick={() => handleAutoResolve('source')}
              className="bg-indigo-950/80 hover:bg-indigo-900 text-indigo-300 px-3 py-1.5 rounded-lg border border-indigo-500/20 font-bold transition-all"
            >
              Keep All Active
            </button>
            <button
              onClick={() => handleAutoResolve('target')}
              className="bg-indigo-600 hover:bg-indigo-500 text-white px-3 py-1.5 rounded-lg border border-indigo-500/30 font-bold transition-all shadow-[0_0_10px_rgba(99,102,241,0.2)]"
            >
              Accept All Incoming
            </button>
          </div>
        </div>

        {/* Conflict list container */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4 bg-slate-950/20">
          {conflicts.map((item) => {
            const isSourceSelected = item.selectedChoice === 'source';
            const isTargetSelected = item.selectedChoice === 'target';

            return (
              <div 
                key={item.id} 
                className="bg-[#111A2E]/70 border border-slate-800 hover:border-indigo-500/15 rounded-2xl p-4 transition-all"
              >
                {/* Meta details */}
                <div className="flex items-center justify-between mb-3">
                  <div className="flex items-center gap-2.5">
                    {getConflictBadge(item.type)}
                    <ChevronRight className="w-3.5 h-3.5 text-zinc-600 animate-none" />
                    <span className="text-xs font-extrabold text-zinc-300 tracking-wide">
                      {item.tableName}
                      {item.columnName && <span className="text-zinc-500 font-normal"> ➔ {item.columnName}</span>}
                    </span>
                  </div>
                </div>

                {/* Side-by-Side comparison cards */}
                <div className="grid grid-cols-2 gap-4">
                  {/* Left Choice: Keep Source (Active Branch) */}
                  <div 
                    onClick={() => updateConflictChoice(item.id, 'source')}
                    className={`border-2 rounded-xl p-3 cursor-pointer transition-all flex flex-col justify-between ${
                      isSourceSelected 
                        ? 'border-indigo-500 bg-indigo-500/5 shadow-[0_0_12px_rgba(99,102,241,0.1)]' 
                        : 'border-slate-800 bg-slate-900/40 opacity-70 hover:opacity-100 hover:border-zinc-700'
                    }`}
                  >
                    <div className="flex justify-between items-center mb-2">
                      <span className="text-[10px] font-bold text-indigo-400 uppercase tracking-widest">Active Branch (Current)</span>
                      {isSourceSelected && <CheckCircle2 className="w-4 h-4 text-indigo-400 animate-none" />}
                    </div>

                    <div className="text-xs font-mono text-zinc-300 bg-black/25 p-2 rounded-lg border border-slate-800/45 min-h-[44px] flex items-center">
                      {item.sourceValue ? (
                        typeof item.sourceValue === 'string' ? (
                          <span>{item.sourceValue}</span>
                        ) : (
                          <div className="flex flex-col gap-1 w-full">
                            <span className="font-bold text-zinc-100">{item.sourceValue.name}</span>
                            <span className="text-[10px] text-zinc-500">
                              {item.sourceValue.type ? `${item.sourceValue.type} ${item.sourceValue.isPK ? '[PK]' : ''} ${item.sourceValue.isFK ? '[FK]' : ''}` : `${item.sourceValue.columns?.length || 0} Columns`}
                            </span>
                          </div>
                        )
                      ) : (
                        <span className="text-rose-500 italic font-sans font-semibold">Not in This Branch</span>
                      )}
                    </div>
                  </div>

                  {/* Right Choice: Keep Target (Incoming Branch) */}
                  <div 
                    onClick={() => updateConflictChoice(item.id, 'target')}
                    className={`border-2 rounded-xl p-3 cursor-pointer transition-all flex flex-col justify-between ${
                      isTargetSelected 
                        ? 'border-emerald-500 bg-emerald-500/5 shadow-[0_0_12px_rgba(16,185,129,0.1)]' 
                        : 'border-slate-800 bg-slate-900/40 opacity-70 hover:opacity-100 hover:border-zinc-700'
                    }`}
                  >
                    <div className="flex justify-between items-center mb-2">
                      <span className="text-[10px] font-bold text-emerald-400 uppercase tracking-widest">Incoming Branch (Source)</span>
                      {isTargetSelected && <CheckCircle2 className="w-4 h-4 text-emerald-400 animate-none" />}
                    </div>

                    <div className="text-xs font-mono text-zinc-300 bg-black/25 p-2 rounded-lg border border-slate-800/45 min-h-[44px] flex items-center">
                      {item.targetValue ? (
                        typeof item.targetValue === 'string' ? (
                          <span>{item.targetValue}</span>
                        ) : (
                          <div className="flex flex-col gap-1 w-full">
                            <span className="font-bold text-zinc-100">{item.targetValue.name}</span>
                            <span className="text-[10px] text-zinc-500">
                              {item.targetValue.type ? `${item.targetValue.type} ${item.targetValue.isPK ? '[PK]' : ''} ${item.targetValue.isFK ? '[FK]' : ''}` : `${item.targetValue.columns?.length || 0} Columns`}
                            </span>
                          </div>
                        )
                      ) : (
                        <span className="text-rose-500 italic font-sans font-semibold">Not in This Branch</span>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Footer actions */}
        <div className="bg-slate-900/90 border-t border-indigo-500/20 px-6 py-4 flex items-center justify-between">
          <button
            onClick={resetMergeSession}
            className="px-5 py-2.5 rounded-xl border border-zinc-700 text-zinc-400 hover:text-white hover:bg-zinc-800 text-sm font-semibold transition-colors animate-none"
          >
            Cancel
          </button>
          
          <button
            onClick={handleCompleteMerge}
            className="bg-gradient-to-r from-[#4f46e5] to-[#6366f1] hover:from-[#5b4ff8] hover:to-[#818cf8] text-white px-6 py-2.5 rounded-xl text-sm font-extrabold shadow-[0_0_20px_rgba(79,70,229,0.5)] flex items-center gap-2 transition-all animate-none"
          >
            <GitMerge className="w-4.5 h-4.5" />
            <span>Apply Decisions & Complete Merge</span>
          </button>
        </div>

      </div>
    </div>
  );
}
