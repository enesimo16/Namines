import React, { useState } from 'react';
import { GitBranch, Plus, Trash2, Eye, EyeOff, GitMerge, ChevronDown } from 'lucide-react';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { confirmDialog } from '../../../store/useConfirmStore';
import { useBranchStore } from '../../../store/useBranchStore';
import { calculateSchemaDiff } from '../../../utils/schemaDiff';
import { useToastStore } from '../../../store/useToastStore';
import ContextualHelpTooltip from '../../help/ContextualHelpTooltip';
import { helpContent } from '../../../lib/helpContent';

export default function BranchControlPanel() {
  const { projects, activeProjectId, createBranch, switchBranch, deleteBranch } = useProjectHistoryStore();
  const { schema, loadFromSchema } = useSchemaStore();
  
  const { 
    compareBranchName, 
    isDiffMode, 
    setCompareBranchName, 
    setIsDiffMode,
    startMergeSession
  } = useBranchStore();

  const [isOpen, setIsOpen] = useState(false);
  const [newBranchName, setNewBranchName] = useState('');
  const [showCreateInput, setShowCreateInput] = useState(false);
  const showToast = useToastStore(state => state.showToast);

  const activeProject = projects.find(p => p.id === activeProjectId);
  if (!activeProject) return null;

  const currentBranchName = activeProject.currentBranch || 'main';
  const branches = activeProject.branches || [];

  const triggerToast = (msg: string, type: 'success' | 'info' | 'warning' | 'error' = 'info') => {
    showToast(msg, type);
  };

  const handleCreateBranch = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newBranchName.trim()) return;
    
    const sanitized = newBranchName.trim().toLowerCase().replace(/[^a-z0-9\-_/]/g, '-');
    
    const created = createBranch(activeProject.id, sanitized);
    if (created) {
      loadFromSchema(created.schema, created.nodePositions);
      triggerToast(`Branch '${sanitized}' created and switched!`, 'success');
      setNewBranchName('');
      setShowCreateInput(false);
    }
  };

  const handleSwitchBranch = (name: string) => {
    if (name === currentBranchName) return;
    const switched = switchBranch(activeProject.id, name);
    if (switched) {
      loadFromSchema(switched.schema, switched.nodePositions);
      triggerToast(`Switched to branch '${name}'!`, 'success');
      setIsDiffMode(false); 
      setIsOpen(false);
    }
  };

  const handleDeleteBranch = async (e: React.MouseEvent, name: string) => {
    e.stopPropagation();
    if (name === 'main') return;

    const ok = await confirmDialog({
      title: 'Delete branch',
      message: `Are you sure you want to delete branch '${name}'? This action cannot be undone.`,
      confirmLabel: 'Delete',
      danger: true,
    });
    if (ok) {
      deleteBranch(activeProject.id, name);
      triggerToast(`Branch '${name}' deleted.`, 'success');

      if (currentBranchName === name) {
        const mainBranch = branches.find(b => b.name === 'main');
        if (mainBranch) {
          loadFromSchema(mainBranch.schema, mainBranch.nodePositions);
        }
      }
    }
  };

  const handleToggleDiffMode = () => {
    if (isDiffMode) {
      setIsDiffMode(false);
      triggerToast("Diff view disabled.", "info");
    } else {
      if (!compareBranchName) {
        const otherBranch = branches.find(b => b.name !== currentBranchName);
        if (otherBranch) {
          setCompareBranchName(otherBranch.name);
        } else {
          triggerToast("No other branch to compare with. Create a new branch!", "warning");
          return;
        }
      }
      setIsDiffMode(true);
      triggerToast("Diff view active!", "info");
    }
  };

  const handleStartMerge = () => {
    if (!schema) {
      triggerToast("Failed to load active schema.", "error");
      return;
    }

    const targetBranchName = compareBranchName || branches.find(b => b.name !== currentBranchName)?.name;
    if (!targetBranchName) {
      triggerToast("You must select a branch to merge with.", "warning");
      return;
    }

    const targetBranch = branches.find(b => b.name === targetBranchName);
    if (!targetBranch) return;

    const diffResult = calculateSchemaDiff(schema, targetBranch.schema);

    if (!diffResult.hasChanges) {
      triggerToast("Branches are already identical! No merge needed.", "info");
      return;
    }

    const conflictsList: any[] = [];

    Object.entries(diffResult.tables).forEach(([tableId, tableDiff]) => {
      if (tableDiff.status === 'added') {
        const activeTable = schema.tables.find(t => t.id === tableId);
        conflictsList.push({
          id: tableId,
          type: 'table_added',
          tableName: activeTable?.name || 'New Table',
          sourceValue: activeTable,
          targetValue: null,
          selectedChoice: 'source'
        });
      } else if (tableDiff.status === 'deleted') {
        const deletedTable = targetBranch.schema.tables.find(t => t.id === tableId);
        conflictsList.push({
          id: tableId,
          type: 'table_deleted',
          tableName: deletedTable?.name || 'Deleted Table',
          sourceValue: null,
          targetValue: deletedTable,
          selectedChoice: 'source'
        });
      } else if (tableDiff.status === 'modified') {
        if (tableDiff.nameChanged) {
          conflictsList.push({
            id: `${tableId}-name`,
            type: 'table_name',
            tableName: tableDiff.newName || 'Table',
            sourceValue: tableDiff.newName,
            targetValue: tableDiff.oldName,
            selectedChoice: 'source'
          });
        }

        Object.entries(tableDiff.columns).forEach(([colId, colDiff]) => {
          const activeTable = schema.tables.find(t => t.id === tableId);
          const compareTable = targetBranch.schema.tables.find(t => t.id === tableId);

          if (colDiff.status === 'added') {
            const col = activeTable?.columns.find(c => c.id === colId);
            conflictsList.push({
              id: `${tableId}-${colId}-added`,
              type: 'column_added',
              tableName: activeTable?.name || 'Table',
              columnName: col?.name,
              sourceValue: col,
              targetValue: null,
              selectedChoice: 'source'
            });
          } else if (colDiff.status === 'deleted') {
            const col = compareTable?.columns.find(c => c.id === colId) || colDiff.details?.oldColumn;
            conflictsList.push({
              id: `${tableId}-${colId}-deleted`,
              type: 'column_deleted',
              tableName: activeTable?.name || compareTable?.name || 'Table',
              columnName: col?.name,
              sourceValue: null,
              targetValue: col,
              selectedChoice: 'source'
            });
          } else if (colDiff.status === 'modified') {
            const activeCol = activeTable?.columns.find(c => c.id === colId);
            const compareCol = compareTable?.columns.find(c => c.id === colId);
            conflictsList.push({
              id: `${tableId}-${colId}-modified`,
              type: 'column_modified',
              tableName: activeTable?.name || 'Table',
              columnName: activeCol?.name,
              sourceValue: activeCol,
              targetValue: compareCol,
              selectedChoice: 'source'
            });
          }
        });
      }
    });

    startMergeSession(targetBranchName, currentBranchName, conflictsList);
  };

  return (
    <div className="mt-3 border-t border-indigo-500/10 pt-3 flex flex-col gap-2 w-full relative">
      <div className="flex items-center justify-between px-1">
        <span className="text-[10px] font-extrabold text-indigo-400/70 tracking-widest uppercase">Version Control</span>
        <ContextualHelpTooltip content={helpContent.branching} />
      </div>

      {/* Main Branch Selector Button */}
      <div className="flex gap-2 w-full">
        <button
          onClick={() => setIsOpen(!isOpen)}
          className="flex-1 flex items-center justify-between gap-2 text-indigo-300 hover:text-white transition-colors font-bold text-xs tracking-wide bg-indigo-500/10 hover:bg-indigo-500/20 px-3 py-2 rounded-xl border border-indigo-500/20 min-w-0"
        >
          <div className="flex items-center gap-1.5 truncate">
            <GitBranch className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
            <span className="truncate">{currentBranchName}</span>
          </div>
          <ChevronDown className={`w-3.5 h-3.5 shrink-0 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`} />
        </button>

        {/* Diff Mode Toggle Button */}
        <button
          onClick={handleToggleDiffMode}
          className={`p-2 rounded-xl transition-all border flex items-center justify-center shrink-0 ${
            isDiffMode
              ? 'bg-emerald-500/20 border-emerald-500/40 text-emerald-300 hover:bg-emerald-500/30 shadow-[0_0_12px_rgba(16,185,129,0.2)]'
              : 'bg-zinc-800/40 border-zinc-700/50 text-zinc-400 hover:text-white hover:bg-zinc-800/80'
          }`}
          title={isDiffMode ? "Close Diff View" : "Compare Differences"}
        >
          {isDiffMode ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
        </button>

        {/* Merge Button */}
        {isDiffMode && (
          <button
            onClick={handleStartMerge}
            className="bg-indigo-600 hover:bg-indigo-500 text-white p-2 rounded-xl border border-indigo-400/30 flex items-center justify-center transition-all shrink-0 shadow-[0_0_12px_rgba(99,102,241,0.4)]"
            title="Merge branch into current branch"
          >
            <GitMerge className="w-4 h-4" />
          </button>
        )}
      </div>

      {/* Expandable Dropdown Menu */}
      {isOpen && (
        <div className="absolute top-full left-0 w-full mt-1.5 bg-surface-700/95 backdrop-blur-lg border border-indigo-500/25 rounded-2xl shadow-2xl p-3 animate-in fade-in duration-200 z-50">
          <div className="text-[10px] font-extrabold text-indigo-400/70 tracking-widest uppercase mb-2 px-1">Branches</div>
          
          <div className="flex flex-col gap-1 max-h-36 overflow-y-auto mb-2 pr-1">
            {branches.map(b => (
              <div
                key={b.name}
                onClick={() => handleSwitchBranch(b.name)}
                className={`group flex items-center justify-between px-2.5 py-1.5 rounded-lg cursor-pointer border text-[11px] font-medium transition-all ${
                  b.name === currentBranchName
                    ? 'bg-indigo-600/20 border-indigo-500/40 text-indigo-200'
                    : 'bg-zinc-900/45 border-transparent text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/40'
                }`}
              >
                <div className="flex items-center gap-1.5 truncate">
                  <GitBranch className={`w-3 h-3 ${b.name === currentBranchName ? 'text-indigo-400' : 'text-zinc-600'}`} />
                  <span className="truncate">{b.name}</span>
                </div>
                
                {b.name !== 'main' && (
                  <button
                    onClick={(e) => handleDeleteBranch(e, b.name)}
                    className="opacity-0 group-hover:opacity-100 p-0.5 hover:bg-rose-500/20 rounded text-zinc-500 hover:text-rose-400 transition-all"
                    title="Delete Branch"
                  >
                    <Trash2 className="w-3 h-3" />
                  </button>
                )}
              </div>
            ))}
          </div>

          <div className="h-[1px] bg-zinc-800/80 my-2" />

          {/* New Branch Creation Form */}
          {showCreateInput ? (
            <form onSubmit={handleCreateBranch} className="flex gap-1">
              <input
                type="text"
                placeholder="Branch name..."
                value={newBranchName}
                onChange={(e) => setNewBranchName(e.target.value)}
                autoFocus
                className="bg-zinc-900 border border-zinc-700 rounded-lg px-2 py-1 text-[11px] text-zinc-100 focus:outline-none focus:border-indigo-500 flex-1 min-w-0"
              />
              <button
                type="submit"
                className="bg-indigo-600 hover:bg-indigo-500 text-white px-2 py-1 rounded-lg text-[11px] font-bold"
              >
                Add
              </button>
            </form>
          ) : (
            <button
              onClick={() => setShowCreateInput(true)}
              className="w-full flex items-center justify-center gap-1 py-1.5 rounded-lg border border-dashed border-zinc-700 hover:border-indigo-500/40 text-zinc-400 hover:text-indigo-300 text-[11px] font-semibold transition-all bg-zinc-900/25"
            >
              <Plus className="w-3 h-3" />
              <span>Create Branch</span>
            </button>
          )}

          {/* Diff comparison selector when diffing */}
          {isDiffMode && (
            <div className="mt-2 pt-2 border-t border-zinc-800/80">
              <label className="text-[9px] font-extrabold text-zinc-500 block mb-1 uppercase tracking-wide">Compare Branch</label>
              <select
                value={compareBranchName || ''}
                onChange={(e) => setCompareBranchName(e.target.value || null)}
                className="w-full bg-zinc-900 border border-zinc-700 rounded-lg px-2 py-1 text-[11px] text-zinc-300 focus:outline-none focus:border-indigo-500"
              >
                {branches
                  .filter(b => b.name !== currentBranchName)
                  .map(b => (
                    <option key={b.name} value={b.name}>
                      {b.name}
                    </option>
                  ))}
              </select>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
