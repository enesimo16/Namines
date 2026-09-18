'use client';

import React, { useRef } from 'react';
import { X, LayoutTemplate } from 'lucide-react';
import { useFocusTrap } from '../../hooks/useFocusTrap';
import { TEMPLATES, TEMPLATE_SIZES, type SchemaTemplate } from '../../lib/templates';

interface StarterPickerModalProps {
  onPick: (template: SchemaTemplate) => void;
  onClose: () => void;
}

/**
 * Hazır şemadan başlama ekranı.
 *
 * <b>Ürünün prompt YAZMADAN kullanılabildiği en kısa yol.</b> Hiçbir ağ
 * çağrısı, hiçbir yapay zekâ, hiçbir kota yok: şemalar `lib/templates.ts`
 * içinde hazır duruyor ve seçim doğrudan canvas'a gidiyor.
 *
 * <b>Liste elle yazılmıyor</b>, katalogdan geliyor: yeni bir şablon
 * eklendiğinde burada değiştirilecek bir şey olmaması gerekiyor — aksi hâlde
 * şablon ürüne girer ama bu ekranda hiç görünmez.
 */
export function StarterPickerModal({ onPick, onClose }: StarterPickerModalProps) {
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(true, modalRef);

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/80 backdrop-blur-sm">
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-label="Start from a ready-made schema"
        className="w-full max-w-2xl max-h-[85vh] overflow-y-auto glass-panel rounded-[var(--radius-modal)] p-5 flex flex-col gap-4"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2">
            <LayoutTemplate className="w-4 h-4 text-content-secondary" />
            <h2 className="text-body-lg text-content-primary">Start from a ready-made schema</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="p-1 text-content-muted hover:text-content-primary transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <p className="text-micro text-content-muted">
          Pick one and start editing on the canvas — no prompt, no generation, nothing to wait for.
        </p>

        {TEMPLATE_SIZES.map(size => {
          const items = TEMPLATES.filter(t => t.size === size.id);
          if (items.length === 0) return null;

          return (
            <div key={size.id} className="flex flex-col gap-2">
              <div className="flex items-baseline gap-2">
                <span className="text-micro uppercase tracking-wide text-content-muted">{size.label}</span>
                <span className="text-micro text-content-muted">{size.blurb}</span>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                {items.map(template => (
                  <button
                    key={template.key}
                    type="button"
                    onClick={() => onPick(template)}
                    className="flex flex-col items-start gap-1 p-3 rounded-[var(--radius-card)] glass-button text-left hover:bg-surface-700/50 transition-colors"
                  >
                    <span className="text-sm text-content-primary">{template.label}</span>
                    <span className="text-micro text-content-muted">{template.description}</span>
                    {/* Boyut SEÇMEDEN ÖNCE söyleniyor: bu yolda kullanıcının
                        tek bilgisi bu ekran. */}
                    <span className="text-micro text-content-secondary">
                      {template.schema.tables.length} tables · {template.schema.relations.length} relations
                    </span>
                  </button>
                ))}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
