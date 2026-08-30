import React from 'react';
import { Sparkles, X, Plus, Minus, Pencil, ArrowRightLeft } from 'lucide-react';
import { DatabaseSchema } from '../../../types/schema';
import { calculateSchemaDiff } from '../../../utils/schemaDiff';

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
    <div className="fixed inset-0 z-[110] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200">
      <div className="bg-surface-800 border border-content-primary/12 rounded-[var(--radius-modal)] w-[90vw] max-w-2xl max-h-[85vh] flex flex-col shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] overflow-hidden">
        <div className="border-b border-content-primary/10 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 bg-surface-600 border border-content-primary/10 rounded-[var(--radius-control)] flex items-center justify-center">
              <Sparkles className="w-4 h-4 text-content-primary" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">Alternative generated</h2>
              <p className="text-[11px] text-content-muted">Pick one — the other is discarded.</p>
            </div>
          </div>
          <button onClick={onClose} className="p-1.5 hover:bg-white/[0.06] rounded-[var(--radius-control)] text-content-subtle hover:text-content-primary transition-colors" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {!diff.hasChanges ? (
            <p className="text-xs text-content-muted">
              The alternative came out identical to the current schema — nothing to compare.
            </p>
          ) : (
            <div className="flex flex-col gap-4">
              {onlyInAlternative.length > 0 && (
                <DiffGroup icon={<Plus className="w-3.5 h-3.5" />} label="Only in the alternative (B)" tone="success" items={onlyInAlternative.map(t => t.name)} />
              )}
              {onlyInCurrent.length > 0 && (
                <DiffGroup icon={<Minus className="w-3.5 h-3.5" />} label="Only in the current schema (A)" tone="danger" items={onlyInCurrent.map(t => t.name)} />
              )}
              {modified.length > 0 && (
                <DiffGroup icon={<Pencil className="w-3.5 h-3.5" />} label="Changed between A and B" tone="neutral" items={modified.map(t => t.name)} />
              )}
            </div>
          )}

          <p className="text-[11px] text-content-subtle mt-5 flex items-start gap-1.5">
            <ArrowRightLeft className="w-3 h-3 mt-0.5 shrink-0" />
            Both were generated fresh from your original prompt — any manual edits you made
            to the current schema since then aren't reflected in the alternative.
          </p>
        </div>

        <div className="border-t border-content-primary/10 px-5 py-3.5 flex items-center justify-end gap-2.5 shrink-0">
          <button
            onClick={onKeepCurrent}
            className="px-4 py-2 rounded-[var(--radius-control)] border border-content-primary/15 text-content-primary hover:bg-white/[0.06] text-xs font-semibold transition-colors"
          >
            Keep current (A)
          </button>
          <button
            onClick={onKeepAlternative}
            className="bg-content-primary hover:bg-content-secondary text-surface-900 px-4 py-2 rounded-[var(--radius-control)] text-xs font-semibold transition-all"
          >
            Use alternative (B)
          </button>
        </div>
      </div>
    </div>
  );
}

function DiffGroup({ icon, label, tone, items }: { icon: React.ReactNode; label: string; tone: 'success' | 'danger' | 'neutral'; items: string[] }) {
  const toneClass = tone === 'success' ? 'text-success-text' : tone === 'danger' ? 'text-danger-text' : 'text-content-secondary';
  return (
    <div>
      <div className={`flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wide mb-1.5 ${toneClass}`}>
        {icon}
        <span>{label}</span>
        <span className="text-content-subtle font-normal normal-case">({items.length})</span>
      </div>
      <div className="flex flex-wrap gap-1.5">
        {items.map(name => (
          <span key={name} className="font-mono text-xs text-content-primary bg-surface-700 border border-content-primary/10 px-2 py-1 rounded-[var(--radius-control)]">
            {name}
          </span>
        ))}
      </div>
    </div>
  );
}
