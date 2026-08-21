'use client';

import { useState, useEffect } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { X, Plus, Trash2, Key, Link as LinkIcon, AlertTriangle, Check } from 'lucide-react';
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

// Tablo rengi seçenekleri — desatüre, paletle uyumlu. Saf/parlak tonlar yok.
const TABLE_COLORS = [undefined, '#4c5c82', '#7a6a9e', '#a56b8a', '#a6534f', '#5a6b7a', '#4b8a6f', '#4a7f96', '#7a8194'];

const genId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

const inputClass = 'bg-surface-700 border border-content-primary/10 rounded-lg text-content-primary placeholder:text-content-subtle focus:outline-none focus:border-focus-ring transition-colors';

export default function TableEditorDrawer() {
  const { schema, selectedTableForEdit, setSelectedTableForEdit, updateTable } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);

  const originalTable = schema?.tables.find((t: SchemaTable) => t.id === selectedTableForEdit) ?? null;

  const [draft, setDraft] = useState<SchemaTable | null>(null);

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

  const handleTableNameChange = (name: string) => {
    if (!draft) return;
    setDraft({ ...draft, name });
  };

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
        <Dialog.Overlay className="fixed inset-0 bg-black/60 backdrop-blur-sm z-[80] data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0" />

        <Dialog.Content
          className="fixed top-0 right-0 h-full w-[400px] max-w-[92vw] bg-surface-800 border-l border-content-primary/10 shadow-[-4px_0_40px_rgba(0,0,0,0.5)] z-[90] flex flex-col data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right-full data-[state=open]:slide-in-from-right-full duration-250 ease-[cubic-bezier(0.16,1,0.3,1)]"
          aria-describedby="table-editor-desc"
          onInteractOutside={handleClose}
          onEscapeKeyDown={handleClose}
        >
          <Dialog.Description id="table-editor-desc" className="sr-only">
            Edit the table structure
          </Dialog.Description>

          {/* Header — minimal, dekoratif arka plan/glow yok */}
          <div className="px-5 pt-5 pb-4 border-b border-content-primary/10 shrink-0 flex items-center justify-between">
            <div>
              <Dialog.Title className="text-sm font-bold text-content-primary">
                Edit Table
              </Dialog.Title>
              <p className="text-xs text-content-subtle mt-0.5">
                {draft?.columns.length ?? 0} column{draft?.columns.length !== 1 ? 's' : ''}
              </p>
            </div>
            <Dialog.Close asChild>
              <button className="p-1.5 text-content-subtle hover:text-content-primary hover:bg-white/[0.06] rounded-lg transition-colors" aria-label="Close">
                <X className="w-4 h-4" />
              </button>
            </Dialog.Close>
          </div>

          {/* Body */}
          {draft && (
            <div className="flex-1 overflow-y-auto px-5 py-5 flex flex-col gap-5">
              {/* Table Name */}
              <div className="flex flex-col gap-1.5">
                <label className="text-[10px] font-bold tracking-wider text-content-subtle uppercase">Table Name</label>
                <input
                  value={draft.name}
                  onChange={e => handleTableNameChange(e.target.value)}
                  className={`w-full px-3 py-2.5 text-sm ${inputClass}`}
                  placeholder="e.g. users"
                  spellCheck={false}
                />
              </div>

              {/* Table Color */}
              <div className="flex flex-col gap-1.5">
                <label className="text-[10px] font-bold tracking-wider text-content-subtle uppercase">Table Color</label>
                <div className="flex items-center gap-2 flex-wrap">
                  {TABLE_COLORS.map((color) => (
                    <button
                      key={color ?? 'default'}
                      type="button"
                      onClick={() => setDraft({ ...draft, color })}
                      title={color ?? 'Default'}
                      className={`w-6 h-6 rounded-full border transition-all cursor-pointer flex items-center justify-center ${
                        draft.color === color ? 'border-content-primary' : 'border-transparent hover:border-content-primary/40'
                      }`}
                      style={{ backgroundColor: color ?? '#2b3241' }}
                    >
                      {draft.color === color && <Check className="w-3 h-3 text-content-primary" />}
                    </button>
                  ))}
                </div>
              </div>

              {/* Columns */}
              <div className="flex flex-col gap-2">
                <div className="flex items-center justify-between border-b border-content-primary/10 pb-2">
                  <span className="text-[10px] font-bold tracking-wider text-content-subtle uppercase">Columns</span>
                  <button
                    onClick={handleAddColumn}
                    className="flex items-center gap-1 px-2.5 py-1 text-[11px] font-semibold text-content-primary bg-white/[0.08] hover:bg-white/[0.12] border border-white/15 rounded-md transition-all"
                  >
                    <Plus className="w-3 h-3" />
                    <span>Add</span>
                  </button>
                </div>

                <div className="flex flex-col gap-1.5">
                  {draft.columns.map((col) => (
                    <div key={col.id} className="group flex items-center gap-1.5 p-2 bg-surface-700 border border-content-primary/8 rounded-lg hover:border-content-primary/15 transition-all">
                      <div className="flex-shrink-0 w-4 flex justify-center">
                        {col.isPK && <Key className="w-3.5 h-3.5 text-content-primary" />}
                        {col.isFK && !col.isPK && <LinkIcon className="w-3.5 h-3.5 text-content-muted" />}
                        {!col.isPK && !col.isFK && <div className="w-3.5 h-3.5" />}
                      </div>

                      <input
                        value={col.name}
                        onChange={e => handleColumnChange(col.id, 'name', e.target.value)}
                        className={`flex-1 min-w-0 px-2 py-1.5 text-xs font-mono ${inputClass}`}
                        placeholder="column_name"
                        spellCheck={false}
                      />

                      <select
                        value={col.type}
                        onChange={e => handleColumnChange(col.id, 'type', e.target.value)}
                        className={`w-24 flex-shrink-0 px-1.5 py-1.5 text-[11px] appearance-none cursor-pointer ${inputClass}`}
                      >
                        {COLUMN_TYPES.map(t => (
                          <option key={t} value={t}>{t}</option>
                        ))}
                      </select>

                      <div className="flex flex-col gap-1 shrink-0 px-0.5">
                        <label className="flex items-center gap-1 cursor-pointer group/chk">
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
                            <div className="w-3 h-3 border border-content-primary/15 rounded-sm bg-surface-600 peer-checked:bg-accent-hover peer-checked:border-white/25 transition-colors" />
                            <svg className="absolute w-2 h-2 text-content-primary opacity-0 peer-checked:opacity-100 pointer-events-none" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="4"><path d="M5 13l4 4L19 7"/></svg>
                          </div>
                          <span className="text-[9px] font-medium text-content-subtle group-hover/chk:text-content-primary">PK</span>
                        </label>
                        <label className="flex items-center gap-1 cursor-pointer group/chk">
                          <div className="relative flex items-center justify-center">
                            <input
                              type="checkbox"
                              checked={col.isNullable}
                              onChange={e => handleColumnChange(col.id, 'isNullable', e.target.checked)}
                              className="peer sr-only"
                            />
                            <div className="w-3 h-3 border border-content-primary/15 rounded-sm bg-surface-600 peer-checked:bg-accent-hover peer-checked:border-white/25 transition-colors" />
                            <svg className="absolute w-2 h-2 text-content-primary opacity-0 peer-checked:opacity-100 pointer-events-none" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="4"><path d="M5 13l4 4L19 7"/></svg>
                          </div>
                          <span className="text-[9px] font-medium text-content-subtle group-hover/chk:text-content-primary">Null</span>
                        </label>
                      </div>

                      <button
                        onClick={() => handleDeleteColumn(col.id)}
                        disabled={col.isPK && draft.columns.filter((c: SchemaColumn) => c.isPK).length <= 1}
                        className="p-1 text-content-subtle hover:text-danger-text hover:bg-danger-subtle rounded-md opacity-0 group-hover:opacity-100 transition-all disabled:opacity-30 disabled:hover:bg-transparent disabled:hover:text-content-subtle shrink-0"
                        title="Delete column"
                        aria-label="Delete column"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  ))}

                  {draft.columns.length === 0 && (
                    <div className="flex flex-col items-center justify-center py-8 border border-dashed border-content-primary/12 rounded-lg">
                      <p className="text-xs text-content-subtle">No columns added yet.</p>
                      <button
                        onClick={handleAddColumn}
                        className="mt-2 text-[11px] text-content-primary hover:text-content-primary transition-colors"
                      >
                        Add the first column
                      </button>
                    </div>
                  )}
                </div>
              </div>

              {/* ── Index'ler ────────────────────────────────────────────── */}
              <div>
                <div className="flex items-center justify-between mb-2.5">
                  <div>
                    <h3 className="text-xs font-semibold text-content-primary">Index'ler</h3>
                    <p className="text-[10px] text-content-subtle mt-0.5">
                      Sorgu performansı için. FK kolonlarında index önerilir.
                    </p>
                  </div>
                  <button
                    onClick={handleAddIndex}
                    className={`text-[11px] px-2 py-1.5 rounded-md flex items-center gap-1 ${inputClass} hover:border-content-primary/20 text-content-primary`}
                  >
                    <Plus className="w-3 h-3" />
                    Index ekle
                  </button>
                </div>

                {unindexedFkColumns.length > 0 && (
                  <div className="mb-2.5 flex items-start gap-2 p-2.5 rounded-lg bg-surface-600 border border-content-primary/15">
                    <AlertTriangle className="w-3.5 h-3.5 text-content-secondary shrink-0 mt-0.5" />
                    <div className="flex-1 min-w-0">
                      <p className="text-[11px] text-content-secondary">
                        {unindexedFkColumns.map(c => c.name).join(', ')} yabancı anahtar
                        {unindexedFkColumns.length > 1 ? ' kolonlarında' : ' kolonunda'} index yok.
                        Bu, sorgu planında tam tablo taramasına yol açar.
                      </p>
                      <button
                        onClick={handleAddMissingFkIndexes}
                        className="mt-1.5 text-[11px] text-accent-text hover:text-content-primary underline underline-offset-2"
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
                      className="p-2.5 rounded-lg bg-surface-700 border border-content-primary/8"
                    >
                      <div className="flex items-center gap-1.5 mb-2">
                        <input
                          value={ix.name ?? ''}
                          onChange={e => handleIndexChange(ix.id, 'name', e.target.value)}
                          placeholder="(ad otomatik türetilir)"
                          className={`flex-1 min-w-0 px-2 py-1 text-xs font-mono ${inputClass}`}
                        />
                        <label className="flex items-center gap-1.5 text-[10px] text-content-muted shrink-0 cursor-pointer">
                          <input
                            type="checkbox"
                            checked={!!ix.isUnique}
                            onChange={e => handleIndexChange(ix.id, 'isUnique', e.target.checked)}
                            className="accent-accent-hover"
                          />
                          UNIQUE
                        </label>
                        <button
                          onClick={() => handleDeleteIndex(ix.id)}
                          aria-label="Index'i sil"
                          className="p-1 rounded text-content-subtle hover:text-danger-text hover:bg-danger-subtle transition-colors shrink-0"
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
                              className={`px-2 py-0.5 rounded text-[10px] font-mono border transition-colors ${
                                selected
                                  ? 'bg-white/[0.08] border-white/25 text-content-primary'
                                  : 'bg-surface-800 border-content-primary/10 text-content-subtle hover:text-content-primary'
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
                        placeholder="Kısmi index koşulu — ör. DeletedAt IS NULL"
                        className={`mt-2 w-full px-2 py-1 text-[10px] font-mono ${inputClass}`}
                      />
                    </div>
                  ))}

                  {(draft.indexes ?? []).length === 0 && unindexedFkColumns.length === 0 && (
                    <p className="text-[11px] text-content-subtle py-1">Henüz index tanımlanmadı.</p>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Footer */}
          <div className="p-4 border-t border-content-primary/10 bg-surface-800 shrink-0 flex items-center justify-end gap-2">
            <button
              onClick={handleClose}
              className="px-4 py-2 text-xs font-medium text-content-muted hover:text-content-primary hover:bg-white/[0.04] rounded-lg transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              className="px-4 py-2 text-xs font-semibold text-surface-900 bg-content-primary hover:bg-content-secondary rounded-lg transition-all flex items-center gap-2"
            >
              <span>Save Changes</span>
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
