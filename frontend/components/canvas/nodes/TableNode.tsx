import React from 'react';
import { Handle, Position, NodeProps, Node } from '@xyflow/react';
import { Key, Link, Pencil, Plus, Minus, RefreshCw } from 'lucide-react';
import { SchemaTable, SchemaColumn } from '../../../types/schema';
import { useLinterStore } from '../../../store/useLinterStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useDbaStore } from '../../../store/useDbaStore';
import { TableDiff } from '../../../utils/schemaDiff';

export type TableNodeType = Node<{ 
  table: SchemaTable; 
  diff?: TableDiff;
}, 'tableNode'>;

export default function TableNode({ data, selected }: NodeProps<TableNodeType>) {
  const { table, diff } = data;
  const { result } = useLinterStore();
  const { isEditMode, setSelectedTableForEdit } = useSchemaStore();
  
  // Read AI DBA issues for this table safely
  const issues = useDbaStore(state => state.issues);
  const setIsPanelOpen = useDbaStore(state => state.setIsPanelOpen);
  const setSelectedTableFilter = useDbaStore(state => state.setSelectedTableFilter);
  
  const [showPopover, setShowPopover] = React.useState(false);
  
  const dbaIssues = React.useMemo(() => issues.filter(i => i.tableName === table.name), [issues, table.name]);
  const hasDbaError = dbaIssues.some(i => i.severity === 2);
  const hasDbaWarning = dbaIssues.some(i => i.severity === 1);
  const hasError = result?.messages.some(m => m.severity === 2 && m.tableId === table.id);

  // Merge active columns and deleted columns to display them all
  const columnsToRender = React.useMemo(() => {
    if (!diff || diff.status === 'deleted') {
      return table.columns.map(c => ({
        column: c,
        diffStatus: (diff?.status === 'deleted' ? 'deleted' : 'unchanged') as 'added' | 'deleted' | 'modified' | 'unchanged',
        details: undefined as any
      }));
    }

    const list = table.columns.map(c => {
      const colDiff = diff.columns[c.id];
      return {
        column: c,
        diffStatus: (colDiff?.status || 'unchanged') as 'added' | 'deleted' | 'modified' | 'unchanged',
        details: colDiff?.details
      };
    });

    // Add deleted columns (they exist in compare branch but not in active branch)
    Object.entries(diff.columns).forEach(([colId, colDiff]) => {
      if (colDiff.status === 'deleted' && colDiff.details?.oldColumn) {
        if (!list.some(item => item.column.id === colId)) {
          list.push({
            column: colDiff.details.oldColumn,
            diffStatus: 'deleted',
            details: colDiff.details
          });
        }
      }
    });

    return list;
  }, [table, diff]);

  // Determine styles based on diff state
  let borderColorClass = '';
  let containerBgClass = 'bg-zinc-900';
  let diffBadge = null;

  if (diff) {
    if (diff.status === 'added') {
      borderColorClass = 'border-emerald-500 shadow-[0_0_20px_rgba(16,185,129,0.3)]';
      diffBadge = (
        <span className="text-[10px] font-extrabold px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 flex items-center gap-0.5 shadow-[0_0_8px_rgba(16,185,129,0.2)]">
          <Plus className="w-2.5 h-2.5" /> Yeni
        </span>
      );
    } else if (diff.status === 'deleted') {
      borderColorClass = 'border-rose-900/60 opacity-55 pointer-events-none line-through';
      containerBgClass = 'bg-zinc-950/70';
      diffBadge = (
        <span className="text-[10px] font-extrabold px-2 py-0.5 rounded-full bg-rose-500/10 text-rose-500/80 border border-rose-500/20 flex items-center gap-0.5">
          <Minus className="w-2.5 h-2.5" /> Silindi
        </span>
      );
    } else if (diff.status === 'modified') {
      borderColorClass = 'border-amber-500 shadow-[0_0_20px_rgba(245,158,11,0.3)] animate-pulse';
      diffBadge = (
        <span className="text-[10px] font-extrabold px-2 py-0.5 rounded-full bg-amber-500/20 text-amber-400 border border-amber-500/30 flex items-center gap-0.5 shadow-[0_0_8px_rgba(245,158,11,0.2)]">
          <RefreshCw className="w-2.5 h-2.5 animate-spin" style={{ animationDuration: '3s' }} /> Fark Var
        </span>
      );
    } else {
      borderColorClass = selected ? 'border-indigo-500 shadow-indigo-500/20' : 'border-zinc-700';
    }
  } else {
    // Normal styling
    borderColorClass = hasError || hasDbaError
      ? 'border-red-500 shadow-red-500/20'
      : hasDbaWarning
        ? 'border-amber-500 shadow-amber-500/20'
        : isEditMode
          ? selected
            ? 'border-amber-400 shadow-amber-400/20'
            : 'border-amber-500/50 shadow-amber-500/10'
          : selected
            ? 'border-indigo-500 shadow-indigo-500/20'
            : 'border-zinc-700';
  }

  const handleEditClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedTableForEdit(table.id);
  };

  const handleDbaBadgeClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedTableFilter(table.name);
    setIsPanelOpen(true);
  };

  return (
    <div
      className={`${containerBgClass} border-2 rounded-lg shadow-xl w-80 flex flex-col font-sans overflow-hidden transition-all relative ${borderColorClass}`}
      onDoubleClick={(e) => {
        e.stopPropagation();
        if (!diff) {
          setShowPopover(true);
        }
      }}
    >
      {/* Option B: Double Click Popover Overlay (No Emojis) */}
      {showPopover && (
        <div className="absolute inset-0 z-[45] bg-zinc-950/95 backdrop-blur-md flex flex-col items-center justify-center p-5 gap-3 text-center animate-in fade-in zoom-in-95 duration-200">
          <div className="text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-1">
            Tablo İşlemleri
          </div>
          
          <button
            onClick={(e) => {
              e.stopPropagation();
              setSelectedTableForEdit(table.id);
              setShowPopover(false);
            }}
            className="w-full py-2.5 px-4 bg-indigo-600 hover:bg-indigo-500 text-white rounded-xl text-xs font-bold transition-all shadow-[0_0_15px_rgba(99,102,241,0.3)] border border-indigo-400/20 cursor-pointer"
          >
            Manuel Yapıyı Düzenle
          </button>

          <button
            onClick={(e) => {
              e.stopPropagation();
              setSelectedTableFilter(table.name);
              setIsPanelOpen(true);
              setShowPopover(false);
            }}
            className="w-full py-2.5 px-4 bg-zinc-900 hover:bg-zinc-800 text-emerald-400 hover:text-emerald-300 rounded-xl text-xs font-bold transition-all border border-zinc-800 hover:border-zinc-700 cursor-pointer"
          >
            AI DBA Önerilerine Git
          </button>

          <button
            onClick={(e) => {
              e.stopPropagation();
              setShowPopover(false);
            }}
            className="text-[11px] font-semibold text-zinc-500 hover:text-zinc-300 transition-colors mt-1 underline underline-offset-4 cursor-pointer"
          >
            Kapat
          </button>
        </div>
      )}
      {/* Header */}
      <div className="bg-zinc-800 text-zinc-100 font-bold px-4 py-3 border-b border-zinc-700 flex justify-between items-center relative">
        <div className="flex items-center gap-2">
          <span>{table.name}</span>
          {diffBadge}
          {dbaIssues.length > 0 && !diff && (
            <span
              onClick={handleDbaBadgeClick}
              className={`
                flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-extrabold animate-pulse select-none cursor-pointer
                ${hasDbaError ? 'bg-red-500 text-white shadow-[0_0_12px_rgba(239,68,68,0.65)]' :
                  hasDbaWarning ? 'bg-amber-500 text-zinc-950 shadow-[0_0_12px_rgba(245,158,11,0.65)]' :
                  'bg-sky-500 text-white shadow-[0_0_12px_rgba(14,165,233,0.65)]'}
              `}
              title={`${dbaIssues.length} DBA uyarısı mevcut`}
            >
              ⚠️
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-zinc-400 bg-zinc-700/50 px-2 py-1 rounded">
            {table.columns.length} kol.
          </span>
          {isEditMode && !diff && (
            <button
              onClick={handleEditClick}
              className="p-1 hover:bg-zinc-700 rounded text-zinc-400 hover:text-white transition-colors"
              title="Tabloyu düzenle"
              aria-label="Tabloyu düzenle"
            >
              <Pencil className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </div>

      {/* Columns */}
      <div className="flex flex-col py-1">
        {columnsToRender.map(({ column: col, diffStatus, details }) => {
          // Setup custom styling based on column level diff
          let rowClass = "relative flex items-center justify-between px-4 py-2 hover:bg-zinc-800/50 group transition-all";
          let textStyle = col.isPK ? 'font-semibold text-zinc-200' : 'text-zinc-300';
          let statusIndicator = null;
          let typeLabel = `${col.type}${col.length ? `(${col.length})` : ''}`;

          if (diffStatus === 'added') {
            rowClass += ' bg-emerald-950/20 border-l-2 border-emerald-500';
            textStyle = 'font-semibold text-emerald-400';
            statusIndicator = <Plus className="w-3 h-3 text-emerald-400" />;
          } else if (diffStatus === 'deleted') {
            rowClass += ' bg-rose-950/10 opacity-50 line-through border-l-2 border-rose-500';
            textStyle = 'text-rose-500/80 line-through';
            statusIndicator = <Minus className="w-3 h-3 text-rose-500/80" />;
          } else if (diffStatus === 'modified') {
            rowClass += ' bg-amber-950/20 border-l-2 border-amber-500';
            textStyle = 'font-semibold text-amber-400';
            statusIndicator = <RefreshCw className="w-3 h-3 text-amber-400" />;
            
            if (details?.typeChanged && details?.oldColumn) {
              const oldType = `${details.oldColumn.type}${details.oldColumn.length ? `(${details.oldColumn.length})` : ''}`;
              typeLabel = `${oldType} ➔ ${typeLabel}`;
            }
          }

          return (
            <div key={col.id} className={rowClass}>
              {/* Target Handle (Left) */}
              <Handle
                type="target"
                position={Position.Left}
                id={col.id}
                className="!w-3 !h-3 !bg-indigo-500 !border-2 !border-zinc-900 -ml-1.5 opacity-0 group-hover:opacity-100 transition-opacity"
              />

              <div className="flex items-center gap-2">
                {statusIndicator}
                {col.isPK && <Key className="w-3.5 h-3.5 text-amber-500" />}
                {col.isFK && !col.isPK && <Link className="w-3.5 h-3.5 text-indigo-400" />}
                {!col.isPK && !col.isFK && !statusIndicator && <div className="w-3.5 h-3.5" />}

                <span className={`text-sm ${textStyle}`}>
                  {col.name}
                </span>
              </div>

              <div className="text-xs flex gap-2 items-center">
                <span className={diffStatus === 'modified' && details?.typeChanged ? 'text-amber-300 font-bold bg-amber-500/10 px-1 py-0.5 rounded' : 'text-zinc-500'}>
                  {typeLabel}
                </span>
                {col.isNullable && <span className="text-zinc-600 text-[10px] bg-zinc-800/40 px-1 rounded">NULL</span>}
              </div>

              {/* Source Handle (Right) */}
              <Handle
                type="source"
                position={Position.Right}
                id={col.id}
                className="!w-3 !h-3 !bg-indigo-500 !border-2 !border-zinc-900 -mr-1.5 opacity-0 group-hover:opacity-100 transition-opacity"
              />
            </div>
          );
        })}
      </div>
    </div>
  );
}
