import React, { useState } from 'react';
import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath, useReactFlow } from '@xyflow/react';
import type { ReferentialAction } from '../../../types/schema';

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
    hint: 'Varsayılan. Bağlı kayıt varsa silmeyi engeller.',
  },
  {
    value: 'Restrict',
    label: 'RESTRICT',
    hint: 'NO ACTION gibi, kontrol hemen yapılır. MSSQL/Oracle desteklemez.',
  },
  {
    value: 'Cascade',
    label: 'CASCADE',
    hint: 'Bağlı kayıtları da siler. Veri kaybettirebilir.',
    danger: true,
  },
  {
    value: 'SetNull',
    label: 'SET NULL',
    hint: 'Bağlı kaydın FK kolonunu NULL yapar. Kolon nullable olmalı.',
  },
  {
    value: 'SetDefault',
    label: 'SET DEFAULT',
    hint: 'FK kolonunu DEFAULT değerine çeker. Kolonun default’u olmalı.',
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
  const [open, setOpen] = useState(false);

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
    setOpen(false);
  };

  const badge = SHORT_LABEL[onDelete];

  return (
    <>
      <BaseEdge path={edgePath} markerEnd={markerEnd} style={style} />
      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
            fontSize: 10,
            pointerEvents: 'all',
          }}
          className="nodrag nopan"
        >
          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            title={`Silme davranışı: ${onDelete}\nDeğiştirmek için tıkla`}
            aria-label={`İlişki ayarları. Silme davranışı ${onDelete}`}
            aria-expanded={open}
            className="flex items-center gap-1 bg-surface-700 text-content-primary px-2 py-1 rounded border border-content-primary/12 shadow-md font-mono hover:border-white/25 transition-colors cursor-pointer"
          >
            <span>{label}</span>
            {badge && (
              <span
                className={`px-1 rounded text-[9px] ${
                  onDelete === 'Cascade'
                    ? 'bg-danger-subtle text-danger-text'
                    : 'bg-surface-600 text-content-muted'
                }`}
              >
                {badge}
              </span>
            )}
          </button>

          {open && (
            <div
              role="menu"
              className="absolute left-1/2 top-full mt-1 -translate-x-1/2 z-50 w-64 bg-surface-800 border border-content-primary/12 rounded-lg shadow-xl p-1"
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
                  className={`w-full text-left px-2 py-1.5 rounded text-[11px] transition-colors ${
                    onDelete === opt.value
                      ? 'bg-white/[0.08] text-content-primary'
                      : 'text-content-primary hover:bg-white/[0.04]'
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono">{opt.label}</span>
                    {opt.danger && (
                      <span className="text-[9px] text-danger-text shrink-0">veri kaybı</span>
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
