import React, { useRef, useEffect } from 'react';
import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath, useReactFlow } from '@xyflow/react';
import type { ReferentialAction } from '../../../types/schema';
import { useActiveEdgeMenuStore } from '../../../store/useActiveEdgeMenuStore';

/**
 * FK silme davranışı seçenekleri.
 *
 * Varsayılan NoAction'dır ve öyle kalmalıdır: eskiden tüm ilişkilere koşulsuz
 * CASCADE yazılıyordu, bu da SQL Server'da çalıştırılamayan DDL (Msg 1785) ve
 * diğer motorlarda sessiz veri kaybı üretiyordu.
 */
const ON_DELETE_OPTIONS: {
  value: ReferentialAction;
  label: string;
  hint: string;
  danger?: boolean;
}[] = [
  {
    value: 'NoAction',
    label: 'NO ACTION',
    hint: 'Default. Prevents deletion while dependent rows exist.',
  },
  {
    value: 'Restrict',
    label: 'RESTRICT',
    hint: 'Like NO ACTION, but checked immediately. Not supported by MSSQL/Oracle.',
  },
  {
    value: 'Cascade',
    label: 'CASCADE',
    hint: 'Deletes dependent rows too. Can lose data.',
    danger: true,
  },
  {
    value: 'SetNull',
    label: 'SET NULL',
    hint: 'Sets the dependent row FK column to NULL. The column must be nullable.',
  },
  {
    value: 'SetDefault',
    label: 'SET DEFAULT',
    hint: 'Resets the FK column to its DEFAULT. The column must have one.',
  },
];

/** Etikette gösterilecek kısa gösterim — NO ACTION varsayılan olduğu için gizlenir. */
const SHORT_LABEL: Record<string, string> = {
  Cascade: 'CASCADE',
  SetNull: 'SET NULL',
  SetDefault: 'SET DEF',
  Restrict: 'RESTRICT',
};

export default function RelationEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
  data,
}: EdgeProps) {
  const { setEdges } = useReactFlow();
  const activeEdgeId = useActiveEdgeMenuStore((s) => s.activeEdgeId);
  const setActiveEdgeId = useActiveEdgeMenuStore((s) => s.setActiveEdgeId);
  const closeMenu = useActiveEdgeMenuStore((s) => s.close);

  const isOpen = activeEdgeId === id;
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (e: MouseEvent | TouchEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        closeMenu();
      }
    };

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        closeMenu();
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen, closeMenu]);

  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  const relationType = (data?.relationType as string) || '';
  const onDelete = (data?.onDelete as ReferentialAction) || 'NoAction';

  let label = '1:N';
  if (relationType.toLowerCase() === 'onetoone') label = '1:1';
  else if (relationType.toLowerCase() === 'manytomany') label = 'N:M';

  const setOnDelete = (value: ReferentialAction) => {
    setEdges((edges) =>
      edges.map((e) => (e.id === id ? { ...e, data: { ...e.data, onDelete: value } } : e))
    );
    closeMenu();
  };

  const toggleOpen = (e: React.MouseEvent) => {
    e.stopPropagation();
    setActiveEdgeId(isOpen ? null : id);
  };

  const badge = SHORT_LABEL[onDelete];

  return (
    <>
      <BaseEdge path={edgePath} markerEnd={markerEnd} style={style} />
      <EdgeLabelRenderer>
        <div
          ref={containerRef}
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
            fontSize: 10,
            pointerEvents: 'all',
            zIndex: isOpen ? 10000 : 10,
          }}
          className={`nodrag nopan relative ${isOpen ? 'z-[10000]' : 'z-10'}`}
        >
          <button
            type="button"
            onClick={toggleOpen}
            title={`On delete: ${onDelete}\nClick to change`}
            aria-label={`Relation settings. On delete ${onDelete}`}
            aria-expanded={isOpen}
            className="flex items-center gap-1 bg-surface-700 text-content-primary px-2 py-1 rounded-[var(--radius-control)] border border-content-primary/12 shadow-md font-mono hover:border-white/25 transition-colors cursor-pointer"
          >
            <span>{label}</span>
            {badge && (
              <span
                className={`px-1 rounded-[var(--radius-control)] text-micro ${
                  onDelete === 'Cascade'
                    ? 'bg-danger-subtle text-danger-text'
                    : 'bg-surface-600 text-content-muted'
                }`}
              >
                {badge}
              </span>
            )}
          </button>

          {isOpen && (
            <div
              role="menu"
              className="absolute left-1/2 top-full mt-1.5 -translate-x-1/2 z-[10001] w-64 bg-surface-800/98 backdrop-blur-xl border border-content-primary/20 rounded-[var(--radius-card)] shadow-2xl p-1.5"
            >
              <div className="px-2 py-1.5 text-[10px] uppercase tracking-wide text-content-subtle font-semibold">
                Silinince (ON DELETE)
              </div>
              {ON_DELETE_OPTIONS.map((opt) => (
                <button
                  key={opt.value}
                  type="button"
                  role="menuitemradio"
                  aria-checked={onDelete === opt.value}
                  onClick={() => setOnDelete(opt.value)}
                  className={`w-full text-left px-2 py-1.5 rounded-[var(--radius-control)] text-[11px] transition-colors cursor-pointer ${
                    onDelete === opt.value
                      ? 'bg-white/[0.08] text-content-primary font-semibold'
                      : 'text-content-primary hover:bg-white/[0.04]'
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono">{opt.label}</span>
                    {opt.danger && (
                      <span className="text-micro text-danger-text shrink-0">data loss</span>
                    )}
                  </div>
                  <div className="text-[10px] text-content-subtle mt-0.5 leading-snug">{opt.hint}</div>
                </button>
              ))}
            </div>
          )}
        </div>
      </EdgeLabelRenderer>
    </>
  );
}
