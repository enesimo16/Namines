'use client';

import { useState } from 'react';
import {
  type DeskTable, type DeskColumn,
  fieldKind, normalizeValue, insertableColumns, editableColumns, primaryKey,
} from '../lib/schema';

/**
 * Satır ekleme/düzenleme formu — TAMAMEN kolon meta verisinden üretilir.
 *
 * Burada tablo adına ya da kolon adına özel HİÇBİR kural yok; bir alanın
 * bileşeni, zorunluluğu ve varsayılanı yalnızca `type` / `isNullable` /
 * `isPK` / `references` alanlarından çıkıyor. Ürünün "deterministik" vaadi
 * pratikte bu dosyada yaşıyor.
 */
export default function RowForm({
  table, initial, onCancel, onSubmit,
}: {
  table: DeskTable;
  /** null → ekleme, dolu → düzenleme */
  initial: Record<string, unknown> | null;
  onCancel: () => void;
  onSubmit: (values: Record<string, string | null>) => Promise<void>;
}) {
  const isEdit = initial !== null;
  const columns = isEdit ? editableColumns(table) : insertableColumns(table);
  const pk = primaryKey(table);

  const [values, setValues] = useState<Record<string, string>>(() => {
    const seed: Record<string, string> = {};
    for (const c of columns) {
      const raw = initial?.[c.name];
      seed[c.name] = raw === null || raw === undefined ? '' : String(raw);
    }
    return seed;
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const set = (name: string, v: string) => setValues(prev => ({ ...prev, [name]: v }));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const payload: Record<string, string | null> = {};
      for (const c of columns) payload[c.name] = normalizeValue(c, values[c.name] ?? '');
      await onSubmit(payload);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Kaydedilemedi.');
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onCancel(); }}>
      <form className="dialog" onSubmit={submit}>
        <h2>{isEdit ? 'Satırı düzenle' : 'Yeni satır'}</h2>
        <p className="hint">
          {table.name}
          {isEdit && pk ? ` · ${pk} = ${String(initial?.[pk])}` : ''}
        </p>

        {error && <div className="notice notice-error">{error}</div>}

        {columns.map(c => (
          <Field key={c.name} column={c} value={values[c.name] ?? ''} onChange={v => set(c.name, v)} />
        ))}

        <div className="dialog-actions">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>Vazgeç</button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? 'Kaydediliyor…' : isEdit ? 'Kaydet' : 'Ekle'}
          </button>
        </div>
      </form>
    </div>
  );
}

function Field({ column, value, onChange }: {
  column: DeskColumn; value: string; onChange: (v: string) => void;
}) {
  const kind = fieldKind(column);
  // NOT NULL alan zorunlu. Boş bırakılırsa veritabanı zaten reddederdi; formda
  // işaretlemek, hatayı sunucuya gitmeden göstermeyi mümkün kılıyor.
  const required = !column.isNullable;

  const label = (
    <label htmlFor={`f-${column.name}`}>
      {column.name}
      {required && <span className="req">*</span>}
      <span className="type">
        {column.type}{column.length ? `(${column.length})` : ''}
        {column.references ? ` → ${column.references.table}.${column.references.column}` : ''}
      </span>
    </label>
  );

  if (kind === 'boolean') {
    return (
      <div className="field">
        {label}
        <input
          id={`f-${column.name}`}
          type="checkbox"
          checked={value === 'true' || value === '1' || value.toLowerCase() === 't'}
          onChange={e => onChange(e.target.checked ? 'true' : 'false')}
        />
      </div>
    );
  }

  if (kind === 'textarea') {
    return (
      <div className="field">
        {label}
        <textarea id={`f-${column.name}`} value={value} required={required}
                  onChange={e => onChange(e.target.value)} />
      </div>
    );
  }

  // FK ve tarih/sayı: hepsi tek satırlık giriş. FK için açılır liste, hedef
  // tablodan veri çekmeyi gerektirir — ilk sürümde bilinçli olarak ham değer
  // giriliyor ve hedef, etikette gösteriliyor (bkz. README "kapsam dışı").
  const type =
    kind === 'number' ? 'number' :
    kind === 'date' ? 'date' :
    kind === 'datetime' ? 'datetime-local' : 'text';

  return (
    <div className="field">
      {label}
      <input id={`f-${column.name}`} type={type} value={value} required={required}
             onChange={e => onChange(e.target.value)} />
    </div>
  );
}
