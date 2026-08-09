'use client';

import { useState, useEffect } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { X, Plus, Trash2, Key, Link as LinkIcon } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { SchemaTable, SchemaColumn, SchemaIndex } from '../../types/schema';

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
  const showToast = useToastStore(state => state.showToast);

  // Find selected table
  const originalTable = schema?.tables.find((t: SchemaTable) => t.id === selectedTableForEdit) ?? null;

  // Drawer's local draft state
  const [draft, setDraft] = useState<SchemaTable | null>(null);

  // Reset draft when selectedTableForEdit changes
  useEffect(() => {
    setDraft(originalTable ? JSON.parse(JSON.stringify(originalTable)) : null);
  }, [selectedTableForEdit]); // eslint-disable-line react-hooks/exhaustive-deps

  const isOpen = !!selectedTableForEdit && !!draft;

  /**
   * Değişiklikleri ATMADAN kapat.
   * Drawer'ın açık bir "Save Changes" butonu var; kapatma yollarının sessizce
   * kaydetmesi "Cancel" etiketiyle çelişir ve kullanıcı iptal ettiğini sanırken
   * değişiklikleri commit eder. Kaydetmek yalnızca handleSave'in işi.
   */
  const handleClose = () => {
    setSelectedTableForEdit(null);
  };

  // ── Table Name ──────────────────────────────────────────────────────────────
  const handleTableNameChange = (name: string) => {
    if (!draft) return;
    setDraft({ ...draft, name });
  };

  // ── Column Actions ─────────────────────────────────────────────────────
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

    // İsim mevcut kolonlara göre benzersiz olmalı. Sayaç olarak columns.length
    // kullanmak yetmez: bir kolon silindiğinde uzunluk geri düşer ve üretilen ad
    // hâlâ duran bir kolonla çakışır (ör. column_4 iki kez).
    const existingNames = new Set(draft.columns.map((c: SchemaColumn) => c.name.toLowerCase()));
    let suffix = draft.columns.length + 1;
    while (existingNames.has(`column_${suffix}`.toLowerCase())) suffix++;

    const newCol: SchemaColumn = {
      id: genId(),
      name: `column_${suffix}`,
      type: 'VARCHAR',
      length: 255,
      isPK: false,
      isFK: false,
      isNullable: true,
      defaultValue: null,
      // Diğer tüm kolon üretim yollarında olduğu gibi: schemaDiff kimliği
      // önce stableUuid üzerinden eşler; bu alan yoksa diff isim/id'ye düşer.
      stableUuid: genId(),
    };
    setDraft({ ...draft, columns: [...draft.columns, newCol] });
  };

  const handleDeleteColumn = (colId: string) => {
    if (!draft) return;
    setDraft({ ...draft, columns: draft.columns.filter((c: SchemaColumn) => c.id !== colId) });
  };

  // ── Index yönetimi ────────────────────────────────────────────────────────
  // Index'ler Faz 1'de modelde hiç yoktu. FK kolonunda index olmaması üretimdeki
  // en yaygın performans hatasıdır — bu yüzden eksik olanlar için uyarı gösteriyoruz.

  const handleAddIndex = () => {
    if (!draft) return;
    const firstCol = draft.columns[0];
    if (!firstCol) {
      showToast('Index eklemek için önce en az bir kolon ekleyin.', 'error');
      return;
    }
    const newIndex: SchemaIndex = {
      id: `ix_${Date.now()}`,
      columns: [{ columnId: firstCol.id }],
      isUnique: false,
    };
    setDraft({ ...draft, indexes: [...(draft.indexes ?? []), newIndex] });
  };

  const handleIndexChange = <K extends keyof SchemaIndex>(
    indexId: string,
    field: K,
    value: SchemaIndex[K]
  ) => {
    if (!draft) return;
    setDraft({
      ...draft,
      indexes: (draft.indexes ?? []).map(ix =>
        ix.id === indexId ? { ...ix, [field]: value } : ix
      ),
    });
  };

  const handleToggleIndexColumn = (indexId: string, columnId: string) => {
    if (!draft) return;
    setDraft({
      ...draft,
      indexes: (draft.indexes ?? []).map(ix => {
        if (ix.id !== indexId) return ix;
        const exists = ix.columns.some(c => c.columnId === columnId);
        return {
          ...ix,
          columns: exists
            ? ix.columns.filter(c => c.columnId !== columnId)
            : [...ix.columns, { columnId }],
        };
      }),
    });
  };

  const handleDeleteIndex = (indexId: string) => {
    if (!draft) return;
    setDraft({ ...draft, indexes: (draft.indexes ?? []).filter(ix => ix.id !== indexId) });
  };

  /**
   * FK olarak işaretli ama index'i olmayan kolonlar.
   * Bunlar sorgu planında tam tablo taramasına yol açar.
   */
  const unindexedFkColumns = draft
    ? draft.columns.filter(
        c => c.isFK && !(draft.indexes ?? []).some(ix => ix.columns[0]?.columnId === c.id)
      )
    : [];

  const handleAddMissingFkIndexes = () => {
    if (!draft) return;
    const added: SchemaIndex[] = unindexedFkColumns.map((c, i) => ({
      id: `ix_fk_${Date.now()}_${i}`,
      columns: [{ columnId: c.id }],
      isUnique: false,
    }));
    setDraft({ ...draft, indexes: [...(draft.indexes ?? []), ...added] });
    showToast(`${added.length} yabancı anahtar index'i eklendi.`, 'success');
  };

  /**
   * Kaydetmeden önce doğrula. Bu kontroller olmadan geçersiz bir tablo sessizce
   * şemaya girer ve hata ancak DDL derlemesinde ortaya çıkar.
   */
  const validate = (t: SchemaTable): string | null => {
    if (!t.name.trim()) return 'Tablo adı boş olamaz.';

    if (t.columns.length === 0) return 'Tablo en az bir kolon içermeli.';

    const emptyCol = t.columns.find(c => !c.name.trim());
    if (emptyCol) return 'Kolon adı boş olamaz.';

    const seen = new Set<string>();
    for (const c of t.columns) {
      const key = c.name.trim().toLowerCase();
      if (seen.has(key)) return `'${c.name}' kolonu birden fazla kez tanımlanmış. Kolon adları benzersiz olmalı.`;
      seen.add(key);
    }

    if (!t.columns.some(c => c.isPK)) return 'Tablonun en az bir birincil anahtarı (PK) olmalı.';

    // Index doğrulaması — kolonsuz index geçersiz SQL üretir.
    const emptyIndex = (t.indexes ?? []).find(ix => ix.columns.length === 0);
    if (emptyIndex) return 'Her index en az bir kolon içermeli.';

    // Aynı kolon setine sahip yinelenen index'ler disk ve yazma maliyeti üretir.
    const indexKeys = new Set<string>();
    for (const ix of t.indexes ?? []) {
      const key = ix.columns.map(c => c.columnId).join(',') + (ix.isUnique ? ':u' : '');
      if (indexKeys.has(key)) return 'Aynı kolon setine sahip birden fazla index var.';
      indexKeys.add(key);
    }

    return null;
  };

  const handleSave = () => {
    if (!draft) return;

    const error = validate(draft);
    if (error) {
      showToast(error, 'error');
      return;
    }

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
          className="fixed top-0 right-0 h-full w-[450px] bg-gradient-to-b from-surface-900 to-surface-800 border-l border-indigo-500/20 shadow-[-20px_0_40px_rgba(0,0,0,0.4)] z-[90] flex flex-col data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right-full data-[state=open]:slide-in-from-right-full duration-300 ease-[cubic-bezier(0.16,1,0.3,1)]"
          aria-describedby="table-editor-desc"
          onInteractOutside={handleClose}
          onEscapeKeyDown={handleClose}
        >
          <Dialog.Description id="table-editor-desc" className="sr-only">
            Edit the table structure
          </Dialog.Description>

          {/* Background Exaggerated Sea & Stars */}
          <div className="absolute inset-0 pointer-events-none bg-[url('data:image/svg+xml,%3Csvg%20viewBox=%220%200%20200%20200%22%20xmlns=%22http://www.w3.org/2000/svg%22%3E%3Cfilter%20id=%22noiseFilter%22%3E%3CfeTurbulence%20type=%22fractalNoise%22%20baseFrequency=%220.65%22%20numOctaves=%223%22%20stitchTiles=%22stitch%22/%3E%3C/filter%3E%3Crect%20width=%22100%25%22%20height=%22100%25%22%20filter=%22url(%23noiseFilter)%22/%3E%3C/svg%3E')] opacity-[0.02] mix-blend-overlay" />
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
                  Edit Table
                </Dialog.Title>
                <p className="text-sm text-indigo-200/60 mt-0.5">{draft?.columns.length ?? 0} column{draft?.columns.length !== 1 ? 's' : ''} being configured</p>
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
                <label className="text-xs font-bold tracking-wider text-indigo-300/80 uppercase">Table Name</label>
                <input
                  value={draft.name}
                  onChange={e => handleTableNameChange(e.target.value)}
                  className="w-full bg-surface-800 border border-surface-500 rounded-xl px-4 py-3 text-sm text-zinc-100 focus:outline-none focus:border-indigo-500/60 focus:ring-2 focus:ring-indigo-500/20 transition-all placeholder:text-zinc-600"
                  placeholder="e.g. users"
                  spellCheck={false}
                />
              </div>

              {/* Table Color */}
              <div className="flex flex-col gap-2">
                <label className="text-xs font-bold tracking-wider text-indigo-300/80 uppercase">Table Color</label>
                <div className="flex items-center gap-2 flex-wrap">
                  {[undefined, '#6366f1', '#8b5cf6', '#ec4899', '#e11d48', '#f59e0b', '#10b981', '#0ea5e9', '#64748b'].map((color) => (
                    <button
                      key={color ?? 'default'}
                      type="button"
                      onClick={() => setDraft({ ...draft, color })}
                      title={color ?? 'Default'}
                      className={`w-7 h-7 rounded-full border-2 transition-all cursor-pointer ${
                        draft.color === color
                          ? 'border-white scale-110 shadow-lg'
                          : 'border-surface-500 hover:border-white/60 hover:scale-105'
                      }`}
                      style={{ backgroundColor: color ?? '#3f3f46' }}
                    />
                  ))}
                </div>
              </div>

              {/* Columns */}
              <div className="flex flex-col gap-3">
                <div className="flex items-center justify-between border-b border-surface-500/50 pb-2">
                  <span className="text-xs font-bold tracking-wider text-indigo-300/80 uppercase">Columns</span>
                  <button 
                    onClick={handleAddColumn} 
                    className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-indigo-200 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/20 rounded-lg transition-all"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    <span>Add Column</span>
                  </button>
                </div>

                {/* Column rows */}
                <div className="flex flex-col gap-2 mt-1">
                  {draft.columns.map((col) => (
                    <div key={col.id} className="group flex items-center gap-2 p-2.5 bg-surface-800/60 border border-surface-500/60 rounded-xl hover:border-indigo-500/30 transition-all hover:bg-surface-800">
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
                        className="flex-1 min-w-0 bg-surface-600 border border-surface-500 rounded-lg px-3 py-1.5 text-xs text-zinc-200 focus:outline-none focus:border-indigo-500/50 transition-colors placeholder:text-zinc-600"
                        placeholder="column_name"
                        spellCheck={false}
                      />

                      {/* Type */}
                      <select
                        value={col.type}
                        onChange={e => handleColumnChange(col.id, 'type', e.target.value)}
                        className="w-28 flex-shrink-0 bg-surface-600 border border-surface-500 rounded-lg px-2 py-1.5 text-xs text-zinc-200 focus:outline-none focus:border-indigo-500/50 transition-colors appearance-none cursor-pointer"
                      >
                        {COLUMN_TYPES.map(t => (
                          <option key={t} value={t}>{t}</option>
                        ))}
                      </select>

                      {/* Checkboxes */}
                      <div className="flex flex-col gap-1.5 shrink-0 px-1">
                        <label className="flex items-center gap-1.5 cursor-pointer group/chk">
                          <div className="relative flex items-center justify-center">
                            {/* Son PK'nın işareti kaldırılamaz: tablo anahtarsız kalır ve
                                onu hedefleyen ilişkiler anahtar olmayan bir kolona FK
                                üretir. Delete butonu zaten aynı kuralı uyguluyor. */}
                            <input
                              type="checkbox"
                              checked={col.isPK}
                              disabled={col.isPK && draft.columns.filter((c: SchemaColumn) => c.isPK).length <= 1}
                              onChange={e => handleColumnChange(col.id, 'isPK', e.target.checked)}
                              className="peer sr-only disabled:cursor-not-allowed"
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
                          <span className="text-[10px] font-medium text-zinc-400 group-hover/chk:text-zinc-300">Null</span>
                        </label>
                      </div>

                      {/* Delete */}
                      <button
                        onClick={() => handleDeleteColumn(col.id)}
                        disabled={col.isPK && draft.columns.filter((c: SchemaColumn) => c.isPK).length <= 1}
                        className="p-1.5 text-zinc-600 hover:text-red-400 hover:bg-red-400/10 rounded-md opacity-0 group-hover:opacity-100 transition-all disabled:opacity-30 disabled:hover:bg-transparent disabled:hover:text-zinc-600 shrink-0"
                        title="Delete column"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  ))}

                  {draft.columns.length === 0 && (
                    <div className="flex flex-col items-center justify-center py-10 border border-dashed border-surface-500 rounded-xl bg-surface-800/30">
                      <div className="w-10 h-10 rounded-full bg-surface-600 border border-surface-500 flex items-center justify-center text-zinc-500 mb-3">
                        <Plus className="w-5 h-5" />
                      </div>
                      <p className="text-sm text-zinc-400">No columns added yet.</p>
                      <button
                        onClick={handleAddColumn}
                        className="mt-2 text-xs text-indigo-400 hover:text-indigo-300 transition-colors"
                      >
                        Add the first column
                      </button>
                    </div>
                  )}
                </div>
              </div>

              {/* ── Index'ler ────────────────────────────────────────────── */}
              <div className="mt-6">
                <div className="flex items-center justify-between mb-3">
                  <div>
                    <h3 className="text-sm font-semibold text-zinc-200">Index'ler</h3>
                    <p className="text-[11px] text-zinc-500 mt-0.5">
                      Sorgu performansı için. Yabancı anahtar kolonlarında index olması önerilir.
                    </p>
                  </div>
                  <button
                    onClick={handleAddIndex}
                    className="text-xs px-2.5 py-1.5 rounded-md bg-surface-600 hover:bg-surface-500 border border-surface-500 text-zinc-300 transition-colors flex items-center gap-1.5"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    Index ekle
                  </button>
                </div>

                {unindexedFkColumns.length > 0 && (
                  <div className="mb-3 flex items-start gap-2 p-2.5 rounded-md bg-amber-950/40 border border-amber-800/50">
                    <span className="text-amber-400 text-sm leading-none mt-0.5">⚠</span>
                    <div className="flex-1 min-w-0">
                      <p className="text-[11px] text-amber-200">
                        {unindexedFkColumns.map(c => c.name).join(', ')} yabancı anahtar
                        {unindexedFkColumns.length > 1 ? ' kolonlarında' : ' kolonunda'} index yok.
                        Bu, sorgu planında tam tablo taramasına yol açar.
                      </p>
                      <button
                        onClick={handleAddMissingFkIndexes}
                        className="mt-1.5 text-[11px] text-amber-300 hover:text-amber-100 underline underline-offset-2"
                      >
                        Eksik index'leri ekle
                      </button>
                    </div>
                  </div>
                )}

                <div className="space-y-2">
                  {(draft.indexes ?? []).map(ix => (
                    <div
                      key={ix.id}
                      className="p-3 rounded-md bg-surface-700/50 border border-surface-500/50"
                    >
                      <div className="flex items-center gap-2 mb-2">
                        <input
                          value={ix.name ?? ''}
                          onChange={e => handleIndexChange(ix.id, 'name', e.target.value)}
                          placeholder="(ad otomatik türetilir)"
                          className="flex-1 min-w-0 bg-surface-800 border border-surface-500 rounded px-2 py-1 text-xs text-zinc-200 placeholder:text-zinc-600 font-mono"
                        />
                        <label className="flex items-center gap-1.5 text-[11px] text-zinc-400 shrink-0 cursor-pointer">
                          <input
                            type="checkbox"
                            checked={!!ix.isUnique}
                            onChange={e => handleIndexChange(ix.id, 'isUnique', e.target.checked)}
                            className="accent-indigo-500"
                          />
                          UNIQUE
                        </label>
                        <button
                          onClick={() => handleDeleteIndex(ix.id)}
                          aria-label="Index'i sil"
                          className="p-1 rounded text-zinc-500 hover:text-red-400 hover:bg-red-950/40 transition-colors shrink-0"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>

                      <div className="flex flex-wrap gap-1.5">
                        {draft.columns.map(col => {
                          const selected = ix.columns.some(c => c.columnId === col.id);
                          return (
                            <button
                              key={col.id}
                              onClick={() => handleToggleIndexColumn(ix.id, col.id)}
                              className={`px-2 py-0.5 rounded text-[11px] font-mono border transition-colors ${
                                selected
                                  ? 'bg-indigo-600/25 border-indigo-500/60 text-indigo-200'
                                  : 'bg-surface-800 border-surface-500 text-zinc-500 hover:text-zinc-300'
                              }`}
                            >
                              {col.name || '(adsız)'}
                            </button>
                          );
                        })}
                      </div>

                      <input
                        value={ix.where ?? ''}
                        onChange={e => handleIndexChange(ix.id, 'where', e.target.value)}
                        placeholder="Kısmi index koşulu — ör. DeletedAt IS NULL (MSSQL/PostgreSQL/SQLite)"
                        className="mt-2 w-full bg-surface-800 border border-surface-500 rounded px-2 py-1 text-[11px] text-zinc-300 placeholder:text-zinc-600 font-mono"
                      />
                    </div>
                  ))}

                  {(draft.indexes ?? []).length === 0 && unindexedFkColumns.length === 0 && (
                    <p className="text-[11px] text-zinc-600 py-2">Henüz index tanımlanmadı.</p>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Footer */}
          <div className="relative z-10 p-6 border-t border-surface-500/50 bg-surface-900/80 backdrop-blur-md shrink-0 flex items-center justify-end gap-3">
            <button 
              onClick={handleClose} 
              className="px-5 py-2.5 text-sm font-medium text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/80 rounded-xl transition-colors"
            >
              Cancel
            </button>
            <button 
              onClick={handleSave} 
              className="px-6 py-2.5 text-sm font-semibold text-white bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 rounded-xl shadow-[0_0_20px_rgba(99,102,241,0.25)] hover:shadow-[0_0_25px_rgba(99,102,241,0.4)] transition-all flex items-center gap-2"
            >
              <span>Save Changes</span>
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
