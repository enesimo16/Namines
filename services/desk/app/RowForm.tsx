'use client';

import { useEffect, useState } from 'react';
import {
  type DeskTable, type DeskColumn,
  fieldKind, normalizeValue, insertableColumns, editableColumns, primaryKey,
} from '../lib/schema';

/** D4 §2.1 — FK açılır listesi sonucu: seçenekler, ya da eşik aşıldı, ya da hiç denenmedi. */
export type FkOptionsResult =
  | { kind: 'options'; options: { value: string; label: string }[] }
  | { kind: 'too_many' }
  | { kind: 'error' };

/**
 * Satır ekleme/düzenleme formu — TAMAMEN kolon meta verisinden üretilir.
 *
 * Burada tablo adına ya da kolon adına özel HİÇBİR kural yok; bir alanın
 * bileşeni, zorunluluğu ve varsayılanı yalnızca `type` / `isNullable` /
 * `isPK` / `references` alanlarından çıkıyor. Ürünün "deterministik" vaadi
 * pratikte bu dosyada yaşıyor.
 */
export default function RowForm({
  table, initial, onCancel, onSubmit, fetchFkOptions,
}: {
  table: DeskTable;
  /** null → ekleme, dolu → düzenleme */
  initial: Record<string, unknown> | null;
  onCancel: () => void;
  onSubmit: (values: Record<string, string | null>) => Promise<void>;
  /**
   * D4 §2.1: FK alanı için hedef tablodan okunabilir seçenekler.
   * 200'den fazla satır varsa 'too_many' — ham değer girişine düşülür,
   * sebebi arayüzde yazılır (04-DATA-CRUD.md §2.1 ölçek sınırı).
   */
  fetchFkOptions: (targetTable: string) => Promise<FkOptionsResult>;
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
          <Field key={c.name} column={c} value={values[c.name] ?? ''} onChange={v => set(c.name, v)}
                 fetchFkOptions={fetchFkOptions} />
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

function Field({ column, value, onChange, fetchFkOptions }: {
  column: DeskColumn; value: string; onChange: (v: string) => void;
  fetchFkOptions: (targetTable: string) => Promise<FkOptionsResult>;
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

  if (kind === 'reference' && column.references) {
    return (
      <FkField column={column} value={value} onChange={onChange} required={required} label={label}
               fetchFkOptions={fetchFkOptions} />
    );
  }

  // Tarih/sayı: tek satırlık giriş.
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

/**
 * D4 §2.1 — FK alanı okunabilir bir seçim sunar (ham "3" yerine "Ayşe Demir").
 * Hedefte 200'den fazla satır varsa ham değer girişine düşer, sebebini yazar —
 * 50.000 satırlık bir tabloyu açılır listeye dökmek tarayıcıyı kilitlerdi.
 */
function FkField({ column, value, onChange, required, label, fetchFkOptions }: {
  column: DeskColumn; value: string; onChange: (v: string) => void;
  required: boolean; label: React.ReactNode;
  fetchFkOptions: (targetTable: string) => Promise<FkOptionsResult>;
}) {
  const [result, setResult] = useState<FkOptionsResult | 'loading'>('loading');

  useEffect(() => {
    let cancelled = false;
    setResult('loading');
    fetchFkOptions(column.references!.table)
      .then(r => { if (!cancelled) setResult(r); })
      .catch(() => { if (!cancelled) setResult({ kind: 'error' }); });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [column.references!.table]);

  if (result === 'loading') {
    return (
      <div className="field">
        {label}
        <input id={`f-${column.name}`} type="text" value={value} disabled placeholder="Seçenekler yükleniyor…" />
      </div>
    );
  }

  if (result.kind === 'options') {
    return (
      <div className="field">
        {label}
        <select id={`f-${column.name}`} value={value} required={required}
                onChange={e => onChange(e.target.value)}>
          <option value="">{required ? '— seçin —' : '— boş —'}</option>
          {result.options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </div>
    );
  }

  // 'too_many' ya da 'error' — ham değer girişine düş, sebebini yaz.
  return (
    <div className="field">
      {label}
      <input id={`f-${column.name}`} type="text" value={value} required={required}
             onChange={e => onChange(e.target.value)} />
      <div style={{ fontSize: 10.5, color: 'var(--content-subtle)', marginTop: 4 }}>
        {result.kind === 'too_many'
          ? `${column.references!.table} tablosunda 200'den fazla kayıt var — ham ${column.references!.column} değeri girin.`
          : 'Seçenekler okunamadı — ham değer girin.'}
      </div>
    </div>
  );
}
