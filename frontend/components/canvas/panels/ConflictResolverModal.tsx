import React, { useRef } from 'react';
import { GitMerge, X, ArrowRight, Check, CheckCircle2, ChevronRight, AlertTriangle } from 'lucide-react';
import { useBranchStore } from '../../../store/useBranchStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { DatabaseSchema, SchemaTable, SchemaColumn } from '../../../types/schema';
import { useFocusTrap } from '../../../hooks/useFocusTrap';

export default function ConflictResolverModal() {
  const modalRef = useRef<HTMLDivElement>(null);
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

  // trapping focus when conflict resolver is open
  useFocusTrap(isConflictModalOpen, modalRef);

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

    // Merge relations from both branches and ensure referential integrity
    const allRelations = [
      ...(schema.relations || []),
      ...(sourceBranch.schema.relations || [])
    ];

    const uniqueRelationsMap = new Map<string, any>();
    allRelations.forEach(rel => {
      const key = `${rel.sourceTableId}-${rel.sourceColumnId}-${rel.targetTableId}-${rel.targetColumnId}`;
      uniqueRelationsMap.set(key, rel);
    });

    const mergedRelations = Array.from(uniqueRelationsMap.values()).filter(rel => {
      const sourceTable = mergedSchema.tables.find(t => t.id === rel.sourceTableId);
      const targetTable = mergedSchema.tables.find(t => t.id === rel.targetTableId);
      if (!sourceTable || !targetTable) return false;

      const sourceCol = sourceTable.columns.find(c => c.id === rel.sourceColumnId);
      const targetCol = targetTable.columns.find(c => c.id === rel.targetColumnId);
      return !!(sourceCol && targetCol);
    });

    mergedSchema.relations = mergedRelations;

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
      case 'table_added': return <span className="bg-success-subtle text-success-text border border-success/25 px-2 py-0.5 rounded text-[10px] font-semibold">New Table</span>;
      case 'table_deleted': return <span className="bg-danger-subtle text-danger-text border border-danger/25 px-2 py-0.5 rounded text-[10px] font-semibold">Deleted Table</span>;
      case 'table_name': return <span className="bg-surface-600 text-content-secondary border border-content-primary/15 px-2 py-0.5 rounded text-[10px] font-semibold">Name Change</span>;
      case 'column_added': return <span className="bg-success-subtle text-success-text border border-success/20 px-2 py-0.5 rounded text-[10px]">New Column</span>;
      case 'column_deleted': return <span className="bg-danger-subtle text-danger-text border border-danger/20 px-2 py-0.5 rounded text-[10px]">Deleted Column</span>;
      case 'column_modified': return <span className="bg-surface-600 text-content-secondary border border-content-primary/15 px-2 py-0.5 rounded text-[10px]">Column Modification</span>;
      default: return null;
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-300">
      <div ref={modalRef} role="dialog" aria-modal="true" aria-labelledby="merge-modal-title" className="bg-surface-800 border border-content-primary/12 rounded-2xl w-[90vw] max-w-4xl h-[85vh] flex flex-col shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] overflow-hidden">

        {/* Header */}
        <div className="bg-surface-800 border-b border-content-primary/10 px-5 py-3.5 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 bg-surface-600 border border-content-primary/10 rounded-lg flex items-center justify-center">
              <GitMerge className="w-4 h-4 text-content-primary" />
            </div>
            <div>
              <h2 id="merge-modal-title" className="text-sm font-bold text-content-primary">
                Merge Branches
              </h2>
              <p className="text-[11px] text-content-muted">
                Merging <span className="font-semibold text-content-primary">{mergeSourceBranch}</span> into <span className="font-semibold text-content-primary">{mergeTargetBranch}</span>
              </p>
            </div>
          </div>

          <button
            onClick={resetMergeSession}
            className="p-1.5 hover:bg-white/[0.06] rounded-lg text-content-subtle hover:text-content-primary transition-colors"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Info banner */}
        <div className="bg-surface-700 border-b border-content-primary/10 px-5 py-2.5 flex items-center justify-between text-xs text-content-primary">
          <div className="flex items-center gap-2">
            <AlertTriangle className="w-3.5 h-3.5 text-content-muted" />
            <span><strong>{conflicts.length}</strong> structural changes detected. Choose a version for each row.</span>
          </div>
          <div className="flex gap-2">
            <button
              onClick={() => handleAutoResolve('source')}
              className="bg-surface-600 hover:bg-white/[0.08] text-content-primary px-2.5 py-1.5 rounded-md border border-content-primary/10 font-semibold transition-all text-[11px]"
            >
              Keep All Active
            </button>
            <button
              onClick={() => handleAutoResolve('target')}
              className="bg-content-primary hover:bg-content-secondary text-surface-900 px-2.5 py-1.5 rounded-md font-semibold transition-all text-[11px]"
            >
              Accept All Incoming
            </button>
          </div>
        </div>

        {/* Conflict list container */}
        <div className="flex-1 overflow-y-auto p-5 space-y-3">
          {conflicts.map((item) => {
            const isSourceSelected = item.selectedChoice === 'source';
            const isTargetSelected = item.selectedChoice === 'target';

            return (
              <div
                key={item.id}
                className="bg-surface-700 border border-content-primary/8 rounded-xl p-3.5"
              >
                {/* Meta details */}
                <div className="flex items-center gap-2 mb-2.5">
                  {getConflictBadge(item.type)}
                  <ChevronRight className="w-3 h-3 text-content-subtle" />
                  <span className="text-xs font-bold text-content-primary tracking-wide">
                    {item.tableName}
                    {item.columnName && <span className="text-content-subtle font-normal"> → {item.columnName}</span>}
                  </span>
                </div>

                {/* Side-by-Side comparison cards */}
                <div className="grid grid-cols-2 gap-3">
                  {/* Left Choice: Keep Source (Active Branch) */}
                  <div
                    onClick={() => updateConflictChoice(item.id, 'source')}
                    className={`border rounded-lg p-2.5 cursor-pointer transition-all flex flex-col justify-between ${
                      isSourceSelected
                        ? 'border-focus-ring bg-white/[0.06]'
                        : 'border-content-primary/8 bg-surface-800 opacity-70 hover:opacity-100 hover:border-content-primary/15'
                    }`}
                  >
                    <div className="flex justify-between items-center mb-1.5">
                      <span className="text-[9px] font-bold text-content-muted uppercase tracking-widest">Active (Current)</span>
                      {isSourceSelected && <CheckCircle2 className="w-3.5 h-3.5 text-content-primary" />}
                    </div>

                    <div className="text-xs font-mono text-content-primary bg-scrim/20 p-2 rounded-md min-h-[40px] flex items-center">
                      {item.sourceValue ? (
                        typeof item.sourceValue === 'string' ? (
                          <span>{item.sourceValue}</span>
                        ) : (
                          <div className="flex flex-col gap-1 w-full">
                            <span className="font-semibold text-content-primary">{item.sourceValue.name}</span>
                            <span className="text-[10px] text-content-subtle">
                              {item.sourceValue.type ? `${item.sourceValue.type} ${item.sourceValue.isPK ? '[PK]' : ''} ${item.sourceValue.isFK ? '[FK]' : ''}` : `${item.sourceValue.columns?.length || 0} Columns`}
                            </span>
                          </div>
                        )
                      ) : (
                        <span className="text-danger-text italic font-sans">Not in this branch</span>
                      )}
                    </div>
                  </div>

                  {/* Right Choice: Keep Target (Incoming Branch) */}
                  <div
                    onClick={() => updateConflictChoice(item.id, 'target')}
                    className={`border rounded-lg p-2.5 cursor-pointer transition-all flex flex-col justify-between ${
                      isTargetSelected
                        ? 'border-success bg-success-subtle/60'
                        : 'border-content-primary/8 bg-surface-800 opacity-70 hover:opacity-100 hover:border-content-primary/15'
                    }`}
                  >
                    <div className="flex justify-between items-center mb-1.5">
                      <span className="text-[9px] font-bold text-success-text uppercase tracking-widest">Incoming</span>
                      {isTargetSelected && <CheckCircle2 className="w-3.5 h-3.5 text-success-text" />}
                    </div>

                    <div className="text-xs font-mono text-content-primary bg-scrim/20 p-2 rounded-md min-h-[40px] flex items-center">
                      {item.targetValue ? (
                        typeof item.targetValue === 'string' ? (
                          <span>{item.targetValue}</span>
                        ) : (
                          <div className="flex flex-col gap-1 w-full">
                            <span className="font-semibold text-content-primary">{item.targetValue.name}</span>
                            <span className="text-[10px] text-content-subtle">
                              {item.targetValue.type ? `${item.targetValue.type} ${item.targetValue.isPK ? '[PK]' : ''} ${item.targetValue.isFK ? '[FK]' : ''}` : `${item.targetValue.columns?.length || 0} Columns`}
                            </span>
                          </div>
                        )
                      ) : (
                        <span className="text-danger-text italic font-sans">Not in this branch</span>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Footer actions */}
        <div className="bg-surface-800 border-t border-content-primary/10 px-5 py-3.5 flex items-center justify-between">
          <button
            onClick={resetMergeSession}
            className="px-4 py-2 rounded-lg border border-content-primary/10 text-content-muted hover:text-content-primary hover:bg-white/[0.04] text-xs font-semibold transition-colors"
          >
            Cancel
          </button>

          <button
            onClick={handleCompleteMerge}
            className="bg-content-primary hover:bg-content-secondary text-surface-900 px-4 py-2 rounded-lg text-xs font-semibold flex items-center gap-2 transition-all"
          >
            <GitMerge className="w-3.5 h-3.5" />
            <span>Apply & Complete Merge</span>
          </button>
        </div>

      </div>
    </div>
  );
}
