import React from 'react';
import { Sparkles, X, Plus, Minus, Pencil, ArrowRightLeft } from 'lucide-react';
import { DatabaseSchema } from '../../../types/schema';
import { calculateSchemaDiff } from '../../../utils/schemaDiff';
import { useEscapeKey } from '../../../hooks/useEscapeKey';

interface Props {
  /** Şu an canvas'ta olan şema (A). */
  current: DatabaseSchema;
  /** Yeni üretilen alternatif (B). */
  alternative: DatabaseSchema;
  onKeepCurrent: () => void;
  onKeepAlternative: () => void;
  onClose: () => void;
}

/**
 * second-phase/09-SEMA-ALTERNATIFLERI.md — "Alternatif üret".
 *
 * <b>Yeni bir diff motoru YOK</b> — <c>calculateSchemaDiff</c> zaten branch
 * karşılaştırması için var, burada aynen kullanılıyor. Farkı: sonuç bir MERGE
 * ekranı değil (ConflictResolverModal gibi tablo tablo seçim), tek bir A/B
 * kararı — doc'un kendi tarifi budur: "kullanıcı A'yı ya da B'yi seçer,
 * seçilmeyen atılır."
 */
export default function AlternativeCompareModal({ current, alternative, onKeepCurrent, onKeepAlternative, onClose }: Props) {
  useEscapeKey(true, onClose);
  // calculateSchemaDiff(active, compare): active=current(A), compare=alternative(B).
  // "added" -> yalnızca A'da var, "deleted" -> yalnızca B'de var. Ad bilgisi
  // eklenen/silinen kayıtlarda TableDiff'te tutulmuyor (yalnızca "modified" ve ad
  // değiştiyse doluyor) — id'den geriye tablo adına bakılıyor.
  const diff = calculateSchemaDiff(current, alternative);
  const resolveName = (tableId: string, newName?: string, oldName?: string) => {
    if (newName || oldName) return newName || oldName!;
    const table = current.tables.find(t => t.id === tableId) ?? alternative.tables.find(t => t.id === tableId);
    return table?.name ?? tableId;
  };
  const entries = Object.entries(diff.tables).map(([id, t]) => ({ ...t, name: resolveName(id, t.newName, t.oldName) }));
  const onlyInCurrent = entries.filter(t => t.status === 'added');
  const onlyInAlternative = entries.filter(t => t.status === 'deleted');
  const modified = entries.filter(t => t.status === 'modified');

  return (
    <div className="fixed inset-0 z-[110] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200 select-none">
      <div className="bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] w-[90vw] max-w-2xl max-h-[85vh] flex flex-col shadow-2xl overflow-hidden">
        <div className="border-b border-line bg-surface-800/40 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-7 w-7 bg-surface-700 border border-line-strong rounded-[var(--radius-control)] flex items-center justify-center text-content-primary">
              <Sparkles className="w-3.5 h-3.5" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">Alternative Schema Generated</h2>
              <p className="text-micro text-content-muted">Compare schemas and select your preferred variation</p>
            </div>
          </div>
          <button onClick={onClose} className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {!diff.hasChanges ? (
            <div className="text-center py-8 border border-dashed border-line rounded-[var(--radius-card)] bg-surface-800/30">
              <p className="text-xs text-content-muted">
                The alternative schema is identical to the current design — no differences found.
              </p>
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              {onlyInAlternative.length > 0 && (
                <DiffGroup icon={<Plus className="w-3.5 h-3.5" />} label="Only in Alternative (B)" tone="success" items={onlyInAlternative.map(t => t.name)} />
              )}
              {onlyInCurrent.length > 0 && (
                <DiffGroup icon={<Minus className="w-3.5 h-3.5" />} label="Only in Current (A)" tone="danger" items={onlyInCurrent.map(t => t.name)} />
              )}
              {modified.length > 0 && (
                <DiffGroup icon={<Pencil className="w-3.5 h-3.5" />} label="Modified between A and B" tone="neutral" items={modified.map(t => t.name)} />
              )}
            </div>
          )}

          <div className="mt-5 p-3 rounded-[var(--radius-card)] bg-surface-800/60 border border-line text-micro text-content-muted flex items-start gap-2">
            <ArrowRightLeft className="w-3.5 h-3.5 mt-0.5 shrink-0 text-content-muted" />
            <span>
              Both variations originated from your prompt. Manual edits made to the current canvas since initial generation are preserved only in (A).
            </span>
          </div>
        </div>

        <div className="border-t border-line px-5 py-3 flex items-center justify-end gap-2 shrink-0 bg-surface-800/40">
          <button
            onClick={onKeepCurrent}
            className="px-3.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 border border-line text-content-secondary hover:text-content-primary text-xs font-semibold transition-all cursor-pointer"
          >
            Keep Current (A)
          </button>
          <button
            onClick={onKeepAlternative}
            className="bg-content-primary hover:bg-content-secondary text-surface-900 px-4 py-1.5 rounded-[var(--radius-control)] text-xs font-bold transition-all shadow-sm cursor-pointer"
          >
            Use Alternative (B)
          </button>
        </div>
      </div>
    </div>
  );
}

function DiffGroup({ icon, label, tone, items }: { icon: React.ReactNode; label: string; tone: 'success' | 'danger' | 'neutral'; items: string[] }) {
  const toneClass = tone === 'success' ? 'text-success-text' : tone === 'danger' ? 'text-danger-text' : 'text-content-primary';
  return (
    <div className="p-3.5 rounded-[var(--radius-card)] bg-surface-800/70 border border-line">
      <div className={`flex items-center gap-1.5 text-micro font-bold uppercase tracking-wider mb-2 ${toneClass}`}>
        {icon}
        <span>{label}</span>
        <span className="text-content-muted font-normal">({items.length})</span>
      </div>
      <div className="flex flex-wrap gap-1.5">
        {items.map(name => (
          <span key={name} className="font-mono text-micro text-content-primary bg-surface-700 border border-line px-2.5 py-1 rounded-full font-bold">
            {name}
          </span>
        ))}
      </div>
    </div>
  );
}
