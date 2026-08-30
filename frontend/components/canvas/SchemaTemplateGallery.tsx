'use client';

import React, { useState } from 'react';
import { X, Search } from 'lucide-react';
import { TEMPLATES, TEMPLATE_SIZES, type TemplateSize } from '../../lib/templates';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

type Filter = TemplateSize | 'all';

export default function SchemaTemplateGallery({ isOpen, onClose }: Props) {
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const mergeFromSchema = useSchemaStore(s => s.mergeFromSchema);
  const showToast = useToastStore(s => s.showToast);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<Filter>('all');

  if (!isOpen) return null;

  const filtered = TEMPLATES.filter(t => {
    if (filter !== 'all' && t.size !== filter) return false;
    if (!query.trim()) return true;
    const q = query.toLowerCase();
    return t.label.toLowerCase().includes(q) || t.description.toLowerCase().includes(q);
  });

  const handleReplace = (key: string) => {
    const tpl = TEMPLATES.find(t => t.key === key);
    if (!tpl) return;
    // preserveProjectName: false — "Replace" her şeyi değiştiriyor, adı da.
    // Varsayılan davranış özel bir proje adını KORUYOR (kullanıcı adını
    // koyduysa bir şema yüklemek onu silmemeli), ama şablonda tam tersi
    // isteniyor: 40 tablolu ERP yüklendikten sonra üstte hâlâ "E-Commerce"
    // yazıyordu ve tuvaldeki şemayla ekrandaki ad birbirini tutmuyordu.
    loadFromSchema(tpl.schema, undefined, false);
    showToast(`"${tpl.label}" template loaded onto canvas.`, 'success');
    onClose();
  };

  const handleMerge = (key: string) => {
    const tpl = TEMPLATES.find(t => t.key === key);
    if (!tpl) return;
    mergeFromSchema(tpl.schema);
    showToast(`"${tpl.label}" tables merged into current schema.`, 'success');
    onClose();
  };

  return (
    // Mobil: kenarlardan taşmasın diye p-3, ve yükseklik dvh ile — mobil
    // tarayıcılarda vh, adres çubuğu gizlenene kadar gerçek yüksekliği vermiyor
    // ve modalın altı ekranın dışında kalıyordu.
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-scrim/60 backdrop-blur-sm p-3 sm:p-4">
      <div className="bg-surface-800 border border-surface-500 rounded-[var(--radius-modal)] shadow-2xl w-full max-w-2xl flex flex-col max-h-[90dvh]">

        {/* Header */}
        <div className="flex items-center justify-between px-4 sm:px-6 pt-5 pb-4 border-b border-surface-600">
          {/* Başlıktan ikon kaldırıldı: "Schema Templates" yazısı zaten ne
              olduğunu söylüyor, ikon yalnızca gürültü ekliyordu. İkon, metnin
              YERİNE geçtiğinde ya da metni ayrıştırdığında değerli. */}
          <span className="text-content-primary font-semibold text-base truncate">Schema Templates</span>
          <button
            onClick={onClose}
            aria-label="Close template gallery"
            className="text-content-muted hover:text-content-primary transition-colors shrink-0 w-11 h-11 -mr-2 flex items-center justify-center cursor-pointer"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Search + ölçek süzgeci.
            20 şablonda düz bir liste taranamıyor; ölçek, kullanıcının aslında
            sorduğu soru ("küçük bir başlangıç mı, gerçek bir sistem mi"). */}
        <div className="px-4 sm:px-6 pt-4 pb-3 space-y-2.5">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-content-muted pointer-events-none" />
            <input
              autoFocus
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Search templates…"
              className="w-full bg-surface-900 border border-surface-500 focus:border-accent-hover rounded-[var(--radius-card)] pl-9 pr-4 py-2.5 text-sm text-content-primary placeholder-content-muted outline-none"
            />
          </div>

          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:overflow-visible sm:px-0">
            <div className="flex w-max gap-1.5 sm:w-auto sm:flex-wrap">
              {([{ id: 'all' as const, label: 'All' }, ...TEMPLATE_SIZES]).map(tier => {
                const count = tier.id === 'all'
                  ? TEMPLATES.length
                  : TEMPLATES.filter(t => t.size === tier.id).length;
                return (
                  <button
                    key={tier.id}
                    type="button"
                    onClick={() => setFilter(tier.id)}
                    className={`shrink-0 px-3 min-h-11 sm:min-h-0 sm:py-1.5 rounded-[var(--radius-control)] text-[11px] font-bold transition-colors cursor-pointer ${
                      filter === tier.id
                        ? 'bg-content-primary text-surface-900'
                        : 'bg-surface-700 text-content-muted hover:text-content-primary'
                    }`}
                  >
                    {tier.label}
                    <span className="ml-1.5 opacity-60">{count}</span>
                  </button>
                );
              })}
            </div>
          </div>
        </div>

        {/* Grid */}
        <div className="overflow-y-auto px-4 sm:px-6 pb-6 grid grid-cols-1 sm:grid-cols-2 gap-3">
          {filtered.length === 0 && (
            <p className="sm:col-span-2 text-center text-content-muted text-sm py-8">
              No templates match your search.
            </p>
          )}
          {filtered.map(tpl => (
            <div
              key={tpl.key}
              className="flex flex-col p-4 rounded-[var(--radius-card)] bg-surface-700 border border-surface-500 text-left transition-all"
            >
              <p className="text-content-primary font-semibold text-sm">{tpl.label}</p>
              <p className="text-content-muted text-xs mt-0.5 leading-relaxed">{tpl.description}</p>
              <p className="text-accent-text text-xs mt-2 font-medium">
                {tpl.schema.tables.length} tables · {tpl.schema.relations.length} relations
              </p>
              <div className="flex gap-2 mt-3 pt-3 border-t border-surface-500/60">
                <button
                  onClick={() => handleReplace(tpl.key)}
                  className="flex-1 min-h-11 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold transition-colors cursor-pointer"
                >
                  Replace
                </button>
                <button
                  onClick={() => handleMerge(tpl.key)}
                  className="flex-1 min-h-11 rounded-[var(--radius-control)] bg-surface-600 hover:bg-surface-500 text-content-secondary hover:text-content-primary border border-surface-400 text-xs font-bold transition-colors cursor-pointer"
                >
                  Merge
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
