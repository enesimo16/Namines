import React from 'react';
import type { ColumnDiffDetails } from '../../../utils/schemaDiff';
import { Handle, Position, NodeProps, Node } from '@xyflow/react';
import { Key, Link, Pencil, Plus, Minus, RefreshCw } from 'lucide-react';
import { SchemaTable } from '../../../types/schema';

/**
 * Namines Flow kenarının tablo ucundaki çapası. `app/canvas/page.tsx` bu id'yi
 * `sourceHandle` olarak veriyor — iki taraf aynı sabiti kullanmazsa kenar
 * sessizce çizilmez (bkz. header'daki Handle açıklaması).
 */
export const FLOW_HANDLE_ID = 'namines-flow-source';
import { useLinterStore } from '../../../store/useLinterStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useAutomationStore } from '../../../store/useAutomationStore';
import { useDbaStore } from '../../../store/useDbaStore';
import { TableDiff } from '../../../utils/schemaDiff';

export type TableNodeType = Node<{
  table: SchemaTable;
  diff?: TableDiff;
}, 'tableNode'>;

const KEYBOARD_NUDGE_STEP = 10;

function TableNode({ data, selected }: NodeProps<TableNodeType>) {
  const { table, diff } = data;

  // Dar selector'lar. Selector'suz `useSchemaStore()` tüm state'e abone olur ve
  // store'un kimliği her set()'te değiştiği için sürükleme sırasında (~60/sn)
  // canvas'taki HER tablo yeniden render olurdu.
  const isEditMode = useSchemaStore(s => s.isEditMode);
  const setSelectedTableForEdit = useSchemaStore(s => s.setSelectedTableForEdit);
  const deleteTable = useSchemaStore(s => s.deleteTable);
  const duplicateTable = useSchemaStore(s => s.duplicateTable);
  const onNodesChange = useSchemaStore(s => s.onNodesChange);

  const issues = useDbaStore(state => state.issues);
  const setIsPanelOpen = useDbaStore(state => state.setIsPanelOpen);
  const setSelectedTableFilter = useDbaStore(state => state.setSelectedTableFilter);

  const linterResult = useLinterStore(s => s.result);

  const [showPopover, setShowPopover] = React.useState(false);

  const openPopover = React.useCallback(() => {
    if (!diff) setShowPopover(true);
  }, [diff]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    // Prevent key triggers if popover is open or another element is focused inside the node
    if (e.target !== e.currentTarget) return;

    if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.key)) {
      e.preventDefault();

      // Pozisyonu abone olmadan, yalnızca ihtiyaç anında oku: `nodes`'a abone olmak
      // her node'da O(n) bir find çalıştırıp sürükleme başına O(n²) maliyet çıkarıyordu.
      const nodePosition = useSchemaStore.getState().nodes.find(n => n.id === table.id)?.position;
      if (!nodePosition) return;

      let { x, y } = nodePosition;
      switch (e.key) {
        case 'ArrowUp': y -= KEYBOARD_NUDGE_STEP; break;
        case 'ArrowDown': y += KEYBOARD_NUDGE_STEP; break;
        case 'ArrowLeft': x -= KEYBOARD_NUDGE_STEP; break;
        case 'ArrowRight': x += KEYBOARD_NUDGE_STEP; break;
      }

      onNodesChange([{ id: table.id, type: 'position', position: { x, y } }]);
    } else if (e.key === 'Enter' || e.key === ' ') {
      // Popover'a klavyeyle erişim: daha önce yalnızca çift tıkla açılıyordu.
      e.preventDefault();
      openPopover();
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      e.preventDefault();
      if (isEditMode && !diff) {
        // Tabloya bağlı Namines Flow kuralları da temizlenir — aksi hâlde
        // artık var olmayan bir tabloya bağlı "hayalet" kural kalırdı.
        useAutomationStore.getState().deleteRulesForTable(table.id);
        deleteTable(table.id);
      }
    }
  };

  const dbaIssues = React.useMemo(() => issues.filter(i => i.tableName === table.name), [issues, table.name]);
  const hasDbaError = dbaIssues.some(i => i.severity === 2);
  const hasDbaWarning = dbaIssues.some(i => i.severity === 1);
  const hasError = React.useMemo(
    () => linterResult?.messages.some(m => m.severity === 2 && m.tableId === table.id) ?? false,
    [linterResult, table.id]
  );

  // Merge active columns and deleted columns to display them all
  const columnsToRender = React.useMemo(() => {
    if (!diff || diff.status === 'deleted') {
      return table.columns.map(c => ({
        column: c,
        diffStatus: (diff?.status === 'deleted' ? 'deleted' : 'unchanged') as 'added' | 'deleted' | 'modified' | 'unchanged',
        details: undefined as ColumnDiffDetails | undefined
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

  // Determine styles based on diff state.
  // Yüzeyler surface-* token'larından: canvas lacivert bir palette otururken
  // node'un nötr gri (zinc) olması aynı ekranda iki farklı ton ailesi yaratıyordu.
  let borderColorClass = '';
  let containerBgClass = 'bg-surface-800';
  let diffBadge = null;

  if (diff) {
    if (diff.status === 'added') {
      borderColorClass = 'border-success shadow-[0_0_20px_color-mix(in srgb, var(--color-success) 30%, transparent)]';
      diffBadge = (
        <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-success/20 text-success-text border border-success/30 flex items-center gap-0.5 shadow-[0_0_8px_color-mix(in srgb, var(--color-success) 20%, transparent)]">
          <Plus className="w-2.5 h-2.5" /> New
        </span>
      );
    } else if (diff.status === 'deleted') {
      // opacity-55 + zaten soluk kırmızı üst üste binince metin okunamıyordu;
      // şeffaflık kaldırıldı, ayrım çizgi ve kenarlıkla veriliyor.
      borderColorClass = 'border-danger-subtle/60 pointer-events-none line-through';
      containerBgClass = 'bg-surface-900';
      diffBadge = (
        <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-danger/10 text-danger-text border border-danger/20 flex items-center gap-0.5">
          <Minus className="w-2.5 h-2.5" /> Deleted
        </span>
      );
    } else if (diff.status === 'modified') {
      // animate-pulse kaldırıldı: 20 değişmiş tabloda 20 sonsuz animasyon aynı anda dönüyordu.
      borderColorClass = 'border-line-strong';
      diffBadge = (
        <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-surface-700 text-content-secondary border border-line-strong flex items-center gap-0.5">
          <RefreshCw className="w-2.5 h-2.5" /> Modified
        </span>
      );
    } else {
      borderColorClass = selected ? 'border-focus-ring shadow-focus-ring/20' : 'border-surface-500';
    }
  } else {
    // Normal styling
    borderColorClass = hasError || hasDbaError
      ? 'border-danger shadow-danger/20'
      : hasDbaWarning
        ? 'border-content-primary/30 shadow-none'
        : isEditMode
          ? selected
            ? 'border-focus-ring shadow-focus-ring/20'
            : 'border-accent-hover/50 shadow-accent-hover/10'
          : selected
            ? 'border-focus-ring shadow-focus-ring/20'
            : 'border-surface-500';
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
      tabIndex={0}
      onKeyDown={handleKeyDown}
      role="group"
      aria-label={`Table ${table.name} with ${table.columns.length} columns`}
      // w-80(320px) -> w-72(288px): şema büyüdükçe (bkz. 20 şablon, en büyüğü 40
      // tablo) tuvaldeki her tablo bu genişlikte yer kaplıyordu — "her şey büyük
      // görünüyor" geri bildiriminin en somut kaynağı buydu, çünkü ekranda aynı
      // anda görülebilen tablo sayısını doğrudan sınırlıyordu.
      className={`${containerBgClass} border rounded-[var(--radius-card)] shadow-[0_10px_30px_color-mix(in srgb, var(--color-scrim) 40%, transparent)] w-72 flex flex-col overflow-hidden transition-all relative ${borderColorClass}`}
      onDoubleClick={(e) => {
        e.stopPropagation();
        openPopover();
      }}
    >
      {/* Option B: Double Click Popover Overlay (No Emojis) */}
      {showPopover && (
        <div
          className="absolute inset-0 z-[45] bg-surface-900/95 backdrop-blur-md flex flex-col items-center justify-center p-4 gap-2 text-center animate-in fade-in duration-150"
          onKeyDown={(e) => { if (e.key === 'Escape') { e.stopPropagation(); setShowPopover(false); } }}
        >
          <div className="text-[10px] font-bold text-content-muted uppercase tracking-wider mb-0.5">
            Table Operations
          </div>

          <button
            autoFocus
            onClick={(e) => {
              e.stopPropagation();
              setSelectedTableForEdit(table.id);
              setShowPopover(false);
            }}
            className="w-full py-2 px-3 bg-content-primary hover:bg-content-primary-hover text-surface-900 rounded-[var(--radius-control)] text-xs font-semibold transition-all cursor-pointer"
          >
            Edit Manual Structure
          </button>

          <button
            onClick={(e) => {
              e.stopPropagation();
              setSelectedTableFilter(table.name);
              setIsPanelOpen(true);
              setShowPopover(false);
            }}
            className="w-full py-2 px-3 bg-surface-700 hover:bg-surface-600 text-content-primary rounded-[var(--radius-control)] text-xs font-semibold transition-all border border-line cursor-pointer"
          >
            Go to AI DBA Suggestions
          </button>

          {isEditMode && (
            <button
              onClick={(e) => {
                e.stopPropagation();
                duplicateTable(table.id);
                setShowPopover(false);
              }}
              className="w-full py-2 px-3 bg-surface-700 hover:bg-surface-600 text-content-primary rounded-[var(--radius-control)] text-xs font-semibold transition-all border border-line cursor-pointer"
            >
              Duplicate Table
            </button>
          )}

          <button
            onClick={(e) => {
              e.stopPropagation();
              setShowPopover(false);
            }}
            className="text-xs font-medium text-content-muted hover:text-content-primary transition-colors mt-1 cursor-pointer"
          >
            Close
          </button>
        </div>
      )}

      {/* Header */}
      <div
        className={`${table.color ? '' : 'bg-surface-700/80'} text-content-primary font-bold text-sm px-3 py-2 border-b border-line flex justify-between items-center relative`}
        style={table.color ? { backgroundColor: table.color + '26', borderBottomColor: table.color + '4D' } : {}}
      >
        {table.color && (
          <div className="absolute left-0 top-0 bottom-0 w-1 rounded-l-[var(--radius-card)]" style={{ backgroundColor: table.color }} />
        )}

        <Handle
          type="source"
          position={Position.Right}
          id={FLOW_HANDLE_ID}
          className="!w-2 !h-2 !bg-warning !border-0 !opacity-0"
        />

        <div className="flex items-center gap-1.5 min-w-0">
          <span className="truncate tracking-tight">{table.name}</span>
          {diffBadge}
          {dbaIssues.length > 0 && !diff && (
            <button
              type="button"
              onClick={handleDbaBadgeClick}
              className={`
                flex h-4.5 w-4.5 items-center justify-center rounded-full text-[10px] font-bold select-none cursor-pointer
                ${hasDbaError ? 'bg-danger-subtle text-danger-text border border-danger/30 shadow-xs' :
                  hasDbaWarning ? 'bg-surface-600 border border-line-strong text-content-primary' :
                  'bg-surface-700 text-content-secondary'}
              `}
              title={`${dbaIssues.length} DBA warnings present`}
              aria-label={`${table.name}: ${dbaIssues.length} DBA warnings. Open the DBA panel.`}
            >
              {dbaIssues.length}
            </button>
          )}
        </div>
        <div className="flex items-center gap-1.5">
          <span className="text-[10px] font-medium text-content-muted bg-surface-700 border border-line px-1.5 py-0.5 rounded-full">
            {table.columns.length} cols
          </span>
          {isEditMode && !diff && (
            <button
              onClick={handleEditClick}
              className="p-1 hover:bg-surface-600 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary transition-colors cursor-pointer"
              title="Edit table"
              aria-label={`Edit table ${table.name}`}
            >
              <Pencil className="w-3 h-3" />
            </button>
          )}
        </div>
      </div>

      {/* Columns */}
      <div className="flex flex-col py-1 divide-y divide-line/40">
        {columnsToRender.map(({ column: col, diffStatus, details }) => {
          let rowClass = "relative flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-surface-700/40 group transition-colors";
          let textStyle = col.isPK ? 'font-semibold text-content-primary' : 'text-content-secondary';
          let statusIndicator = null;
          let typeLabel = `${col.type}${col.length ? `(${col.length})` : ''}`;

          if (diffStatus === 'added') {
            rowClass += ' bg-success-subtle/20 border-l-2 border-success';
            textStyle = 'font-semibold text-success-text';
            statusIndicator = <Plus className="w-3 h-3 text-success-text" />;
          } else if (diffStatus === 'deleted') {
            rowClass += ' bg-danger-subtle/10 line-through border-l-2 border-danger';
            textStyle = 'text-danger-text line-through';
            statusIndicator = <Minus className="w-3 h-3 text-danger-text" />;
          } else if (diffStatus === 'modified') {
            rowClass += ' bg-surface-700/30 border-l-2 border-line-strong';
            textStyle = 'font-semibold text-content-primary';
            statusIndicator = <RefreshCw className="w-3 h-3 text-content-secondary" />;

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
                className="!w-2.5 !h-2.5 !bg-content-primary !border-2 !border-surface-800 -ml-1.5 opacity-0 group-hover:opacity-100 transition-opacity"
              />

              <div className="flex items-center gap-1.5 min-w-0">
                {statusIndicator}
                {col.isPK && <Key className="w-3.5 h-3.5 text-content-primary shrink-0" />}
                {col.isFK && !col.isPK && <Link className="w-3.5 h-3.5 text-content-muted shrink-0" />}
                {!col.isPK && !col.isFK && !statusIndicator && <div className="w-3.5 h-3.5 shrink-0" />}

                <span className={`text-xs truncate ${textStyle}`}>
                  {col.name}
                </span>
              </div>

              <div className="text-[11px] flex gap-1.5 items-center shrink-0">
                <span className={diffStatus === 'modified' && details?.typeChanged
                  ? 'text-content-primary font-bold bg-surface-700 px-1 py-0.5 rounded-[var(--radius-control)]'
                  : 'text-content-muted font-mono'}>
                  {typeLabel}
                </span>
                {col.isNullable && (
                  <span className="text-content-muted text-[10px] font-bold bg-surface-700 border border-line px-1.5 py-0.5 rounded-[var(--radius-control)] uppercase select-none">
                    NULL
                  </span>
                )}
              </div>

              {/* Source Handle (Right) */}
              <Handle
                type="source"
                position={Position.Right}
                id={col.id}
                className="!w-2.5 !h-2.5 !bg-content-primary !border-2 !border-surface-800 -mr-1.5 opacity-0 group-hover:opacity-100 transition-opacity"
              />
            </div>
          );
        })}
      </div>
    </div>
  );
}

// memo ŞART: canvas'ta node dizisi her sürükleme karesinde yeniden set edilir.
// memo olmadan 30 tablolu bir şemada tek bir tabloyu sürüklemek saniyede
// binlerce gereksiz render üretir.
export default React.memo(TableNode);
