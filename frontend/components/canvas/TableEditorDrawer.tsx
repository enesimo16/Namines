'use client';

import { useState, useEffect } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { X, Plus, Trash2, Key, Link as LinkIcon } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { SchemaTable, SchemaColumn } from '../../types/schema';

const COLUMN_TYPES = [
  'INT', 'BIGINT', 'SMALLINT', 'TINYINT',
  'VARCHAR', 'NVARCHAR', 'CHAR', 'TEXT', 'NTEXT',
  'DATETIME', 'DATE', 'TIME', 'TIMESTAMP',
  'DECIMAL', 'FLOAT', 'REAL', 'NUMERIC',
  'BIT', 'BOOLEAN',
  'UNIQUEIDENTIFIER', 'UUID',
  'BLOB', 'BINARY', 'VARBINARY',
  'JSON',
];

const genId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

export default function TableEditorDrawer() {
  const { schema, selectedTableForEdit, setSelectedTableForEdit, updateTable } = useSchemaStore();

  // Seçili tabloyu bul
  const originalTable = schema?.tables.find((t: SchemaTable) => t.id === selectedTableForEdit) ?? null;

  // Drawer'ın yerel draft state'i
  const [draft, setDraft] = useState<SchemaTable | null>(null);

  // selectedTableForEdit değiştiğinde draft'ı sıfırla
  useEffect(() => {
    setDraft(originalTable ? JSON.parse(JSON.stringify(originalTable)) : null);
  }, [selectedTableForEdit]); // eslint-disable-line react-hooks/exhaustive-deps

  const isOpen = !!selectedTableForEdit && !!draft;

  const handleClose = () => {
    if (draft && originalTable) {
      // Kapatırken otomatik kaydet
      if (JSON.stringify(draft) !== JSON.stringify(originalTable)) {
        updateTable(draft);
      }
    }
    setSelectedTableForEdit(null);
  };

  // ── Tablo adı ──────────────────────────────────────────────────────────────
  const handleTableNameChange = (name: string) => {
    if (!draft) return;
    setDraft({ ...draft, name });
  };

  // ── Kolon aksiyonları ─────────────────────────────────────────────────────
  const handleColumnChange = <K extends keyof SchemaColumn>(
    colId: string, field: K, value: SchemaColumn[K]
  ) => {
    if (!draft) return;
    setDraft({
      ...draft,
      columns: draft.columns.map((c: SchemaColumn) => c.id === colId ? { ...c, [field]: value } : c),
    });
  };

  const handleAddColumn = () => {
    if (!draft) return;
    const newCol: SchemaColumn = {
      id: genId(),
      name: `kolon_${draft.columns.length + 1}`,
      type: 'VARCHAR',
      length: 255,
      isPK: false,
      isFK: false,
      isNullable: true,
      defaultValue: null,
    };
    setDraft({ ...draft, columns: [...draft.columns, newCol] });
  };

  const handleDeleteColumn = (colId: string) => {
    if (!draft) return;
    setDraft({ ...draft, columns: draft.columns.filter((c: SchemaColumn) => c.id !== colId) });
  };

  const handleSave = () => {
    if (!draft) return;
    updateTable(draft);
    setSelectedTableForEdit(null);
  };

  return (
    <Dialog.Root open={isOpen} onOpenChange={(open) => { if (!open) handleClose(); }}>
      <Dialog.Portal>
        {/* Overlay */}
        <Dialog.Overlay className="fixed inset-0 bg-black/60 backdrop-blur-sm z-[80] data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0" />

        {/* Drawer */}
        <Dialog.Content
          className="fixed top-0 right-0 h-full w-[450px] bg-gradient-to-b from-[#09111F] to-[#0D182A] border-l border-indigo-500/20 shadow-[-20px_0_40px_rgba(0,0,0,0.4)] z-[90] flex flex-col data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right-full data-[state=open]:slide-in-from-right-full duration-300 ease-[cubic-bezier(0.16,1,0.3,1)]"
          aria-describedby="table-editor-desc"
          onInteractOutside={handleClose}
          onEscapeKeyDown={handleClose}
        >
          <Dialog.Description id="table-editor-desc" className="sr-only">
            Tablo yapısını düzenleyin
          </Dialog.Description>

          {/* Background Exaggerated Sea & Stars */}
          <div className="absolute inset-0 pointer-events-none bg-[url('/noise.png')] opacity-[0.02] mix-blend-overlay" />
          <div className="absolute inset-0 pointer-events-none bg-[radial-gradient(ellipse_at_top_right,_var(--tw-gradient-stops))] from-indigo-600/20 via-purple-900/5 to-transparent opacity-80" />
          <div className="absolute inset-0 pointer-events-none bg-[radial-gradient(circle_at_bottom_left,_var(--tw-gradient-stops))] from-teal-500/10 via-transparent to-transparent opacity-60" />
          
          {/* Glowing Orbs */}
          <div className="absolute top-10 right-10 w-32 h-32 bg-indigo-500/20 rounded-full blur-[50px] pointer-events-none" />
          <div className="absolute bottom-40 left-10 w-40 h-40 bg-purple-500/10 rounded-full blur-[60px] pointer-events-none" />

          {/* Decorative Wave SVG (Bottom) */}
          <div className="absolute bottom-0 left-0 w-full h-[40%] pointer-events-none opacity-[0.06] z-0">
            <svg viewBox="0 0 400 150" preserveAspectRatio="none" className="w-full h-full fill-indigo-300">
              <path d="M0,50 C100,150 300,0 400,100 L400,150 L0,150 Z" />
            </svg>
          </div>

          {/* Header */}
          <div className="relative px-6 pt-8 pb-5 border-b border-indigo-500/10 shrink-0 flex items-center justify-between">
            <div className="flex items-center gap-4">
              <div className="w-12 h-12 rounded-2xl border border-indigo-400/20 bg-indigo-900/30 flex items-center justify-center shadow-[0_0_15px_rgba(99,102,241,0.15)] text-indigo-300">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="w-6 h-6">
                  <rect x="3" y="3" width="18" height="18" rx="3"/>
                  <path d="M3 9h18M9 21V9"/>
                </svg>
              </div>
              <div>
                <Dialog.Title className="text-xl font-bold tracking-wide bg-gradient-to-r from-zinc-100 to-zinc-300 bg-clip-text text-transparent">
                  Tablo Düzenle
                </Dialog.Title>
                <p className="text-sm text-indigo-200/60 mt-0.5">{draft?.columns.length ?? 0} kolon yapılandırılıyor</p>
              </div>
            </div>
            <Dialog.Close asChild>
              <button className="p-2 text-zinc-500 hover:text-white hover:bg-white/10 rounded-xl transition-colors">
                <X className="w-5 h-5" />
              </button>
            </Dialog.Close>
          </div>

          {/* Body */}
          {draft && (
            <div className="flex-1 overflow-y-auto px-6 py-6 custom-scrollbar relative z-10 flex flex-col gap-6">
              {/* Table Name */}
              <div className="flex flex-col gap-2">
                <label className="text-xs font-bold tracking-wider text-indigo-300/80 uppercase">Tablo Adı</label>
                <input
                  value={draft.name}
                  onChange={e => handleTableNameChange(e.target.value)}
                  className="w-full bg-[#111928] border border-[#2A3750] rounded-xl px-4 py-3 text-sm text-zinc-100 focus:outline-none focus:border-indigo-500/60 focus:ring-2 focus:ring-indigo-500/20 transition-all placeholder:text-zinc-600"
                  placeholder="örn: kullanicilar"
                  spellCheck={false}
                />
              </div>

              {/* Columns */}
              <div className="flex flex-col gap-3">
                <div className="flex items-center justify-between border-b border-[#2A3750]/50 pb-2">
                  <span className="text-xs font-bold tracking-wider text-indigo-300/80 uppercase">Kolonlar</span>
                  <button 
                    onClick={handleAddColumn} 
                    className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-indigo-200 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/20 rounded-lg transition-all"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    <span>Kolon Ekle</span>
                  </button>
                </div>

                {/* Column rows */}
                <div className="flex flex-col gap-2 mt-1">
                  {draft.columns.map((col) => (
                    <div key={col.id} className="group flex items-center gap-2 p-2.5 bg-[#111928]/60 border border-[#2A3750]/60 rounded-xl hover:border-indigo-500/30 transition-all hover:bg-[#111928]">
                      {/* PK/FK Badge */}
                      <div className="flex-shrink-0 w-5 flex justify-center">
                        {col.isPK && <Key className="w-4 h-4 text-amber-500/90 drop-shadow-[0_0_3px_rgba(245,158,11,0.5)]" />}
                        {col.isFK && !col.isPK && <LinkIcon className="w-4 h-4 text-indigo-400 drop-shadow-[0_0_3px_rgba(129,140,248,0.5)]" />}
                        {!col.isPK && !col.isFK && <div className="w-4 h-4" />}
                      </div>

                      {/* Name */}
                      <input
                        value={col.name}
                        onChange={e => handleColumnChange(col.id, 'name', e.target.value)}
                        className="flex-1 min-w-0 bg-[#1A2333] border border-[#2A3750] rounded-lg px-3 py-1.5 text-xs text-zinc-200 focus:outline-none focus:border-indigo-500/50 transition-colors placeholder:text-zinc-600"
                        placeholder="kolon_adi"
                        spellCheck={false}
                      />

                      {/* Type */}
                      <select
                        value={col.type}
                        onChange={e => handleColumnChange(col.id, 'type', e.target.value)}
                        className="w-28 flex-shrink-0 bg-[#1A2333] border border-[#2A3750] rounded-lg px-2 py-1.5 text-xs text-zinc-200 focus:outline-none focus:border-indigo-500/50 transition-colors appearance-none cursor-pointer"
                      >
                        {COLUMN_TYPES.map(t => (
                          <option key={t} value={t}>{t}</option>
                        ))}
                      </select>

                      {/* Checkboxes */}
                      <div className="flex flex-col gap-1.5 shrink-0 px-1">
                        <label className="flex items-center gap-1.5 cursor-pointer group/chk">
                          <div className="relative flex items-center justify-center">
                            <input
                              type="checkbox"
                              checked={col.isPK}
                              onChange={e => handleColumnChange(col.id, 'isPK', e.target.checked)}
                              className="peer sr-only"
                            />
                            <div className="w-3.5 h-3.5 border border-zinc-600 rounded bg-zinc-800 peer-checked:bg-amber-500 peer-checked:border-amber-500 transition-colors" />
                            <svg className="absolute w-2.5 h-2.5 text-white opacity-0 peer-checked:opacity-100 pointer-events-none" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><path d="M5 13l4 4L19 7"/></svg>
                          </div>
                          <span className="text-[10px] font-medium text-zinc-400 group-hover/chk:text-zinc-300">PK</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer group/chk">
                          <div className="relative flex items-center justify-center">
                            <input
                              type="checkbox"
                              checked={col.isNullable}
                              onChange={e => handleColumnChange(col.id, 'isNullable', e.target.checked)}
                              className="peer sr-only"
                            />
                            <div className="w-3.5 h-3.5 border border-zinc-600 rounded bg-zinc-800 peer-checked:bg-indigo-500 peer-checked:border-indigo-500 transition-colors" />
                            <svg className="absolute w-2.5 h-2.5 text-white opacity-0 peer-checked:opacity-100 pointer-events-none" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><path d="M5 13l4 4L19 7"/></svg>
                          </div>
                          <span className="text-[10px] font-medium text-zinc-400 group-hover/chk:text-zinc-300">NN</span>
                        </label>
                      </div>

                      {/* Delete */}
                      <button
                        onClick={() => handleDeleteColumn(col.id)}
                        disabled={col.isPK && draft.columns.filter((c: SchemaColumn) => c.isPK).length <= 1}
                        className="p-1.5 text-zinc-600 hover:text-red-400 hover:bg-red-400/10 rounded-md opacity-0 group-hover:opacity-100 transition-all disabled:opacity-30 disabled:hover:bg-transparent disabled:hover:text-zinc-600 shrink-0"
                        title="Kolonu sil"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  ))}

                  {draft.columns.length === 0 && (
                    <div className="flex flex-col items-center justify-center py-10 border border-dashed border-[#2A3750] rounded-xl bg-[#111928]/30">
                      <div className="w-10 h-10 rounded-full bg-[#1A2333] border border-[#2A3750] flex items-center justify-center text-zinc-500 mb-3">
                        <Plus className="w-5 h-5" />
                      </div>
                      <p className="text-sm text-zinc-400">Henüz kolon eklenmedi.</p>
                      <button 
                        onClick={handleAddColumn}
                        className="mt-2 text-xs text-indigo-400 hover:text-indigo-300 transition-colors"
                      >
                        İlk kolonu ekleyin
                      </button>
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Footer */}
          <div className="relative z-10 p-6 border-t border-[#2A3750]/50 bg-[#09111F]/80 backdrop-blur-md shrink-0 flex items-center justify-end gap-3">
            <button 
              onClick={handleClose} 
              className="px-5 py-2.5 text-sm font-medium text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/80 rounded-xl transition-colors"
            >
              İptal
            </button>
            <button 
              onClick={handleSave} 
              className="px-6 py-2.5 text-sm font-semibold text-white bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 rounded-xl shadow-[0_0_20px_rgba(99,102,241,0.25)] hover:shadow-[0_0_25px_rgba(99,102,241,0.4)] transition-all flex items-center gap-2"
            >
              <span>Değişiklikleri Kaydet</span>
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
