'use client';

import { useCallback, useEffect, useState } from 'react';
import { deskApi, DeskApiError, type DeskRow } from '../lib/api';
import { type DeskTable, primaryKey, isEditable, displayColumn, formatCell } from '../lib/schema';
import RowForm from './RowForm';

const PAGE_SIZE = 25;

/**
 * Namines Desk — ana ekran.
 *
 * Akış: anahtar → şema → tablo seç → satırlar → CRUD. Hiçbir adımda bağlantı
 * dizesi istemciye gelmiyor; sunucu onu anahtardan çözüyor.
 */
export default function Desk({ apiKey, onSignOut }: { apiKey: string; onSignOut: () => void }) {
  const [tables, setTables] = useState<DeskTable[] | null>(null);
  const [active, setActive] = useState<string | null>(null);
  const [rows, setRows] = useState<DeskRow[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Record<string, unknown> | null | undefined>(undefined);

  const table = tables?.find(t => t.name === active) ?? null;

  // Şema bir kez: tablo listesi ve form tanımlarının ikisi de buradan geliyor.
  useEffect(() => {
    let cancelled = false;
    deskApi.schema(apiKey)
      .then(res => { if (!cancelled) { setTables(res.tables); setActive(res.tables[0]?.name ?? null); } })
      .catch(err => { if (!cancelled) setError(err instanceof Error ? err.message : 'Şema okunamadı.'); });
    return () => { cancelled = true; };
  }, [apiKey]);

  const loadRows = useCallback(async (tableName: string, p: number) => {
    setLoading(true);
    setError(null);
    try {
      const res = await deskApi.list(apiKey, tableName, p, PAGE_SIZE);
      setRows(res.rows);
      setTotal(res.totalCount);
    } catch (err) {
      setRows([]);
      setError(err instanceof Error ? err.message : 'Satırlar okunamadı.');
    } finally {
      setLoading(false);
    }
  }, [apiKey]);

  useEffect(() => {
    if (active) { setPage(1); loadRows(active, 1); }
  }, [active, loadRows]);

  async function handleDelete(row: Record<string, unknown>) {
    if (!table) return;
    const pk = primaryKey(table);
    if (!pk) return;
    // Silme geri alınamaz; onay bilinçli olarak satırı adlandırıyor ki
    // kullanıcı hangi kaydı sildiğini görsün.
    const shown = String(row[displayColumn(table)] ?? row[pk]);
    if (!confirm(`"${shown}" kalıcı olarak silinecek. Emin misiniz?`)) return;
    try {
      await deskApi.remove(apiKey, table.name, pk, String(row[pk]));
      await loadRows(table.name, page);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Silinemedi.');
    }
  }

  if (error && !tables) {
    return (
      <div className="gate">
        <div className="gate-card">
          <h1>Namines Desk</h1>
          <div className="notice notice-error">{error}</div>
          <button className="btn" onClick={onSignOut}>Başka bir anahtar dene</button>
        </div>
      </div>
    );
  }

  if (!tables) return <div className="empty">Şema okunuyor…</div>;

  const pk = table ? primaryKey(table) : null;
  const editable = table ? isEditable(table) : false;
  const lastPage = total !== null ? Math.max(1, Math.ceil(total / PAGE_SIZE)) : page;

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar-head">
          <div className="brand">Namines Desk <span className="brand-badge">beta</span></div>
          <div className="sidebar-sub">{tables.length} tablo · salt anahtar erişimi</div>
        </div>
        <nav className="table-list">
          {tables.map(t => (
            <button key={t.name} className="table-item" aria-current={t.name === active}
                    onClick={() => setActive(t.name)}>
              <span>{t.name}</span>
              {!isEditable(t) && <span className="ro">salt-okunur</span>}
            </button>
          ))}
          {tables.length === 0 && <div className="empty">Bu anahtara açılmış tablo yok.</div>}
        </nav>
        <div style={{ padding: 10, borderTop: '1px solid var(--line)' }}>
          <button className="btn btn-sm" style={{ width: '100%' }} onClick={onSignOut}>Anahtarı değiştir</button>
        </div>
      </aside>

      <main className="main">
        <div className="topbar">
          <div>
            <h1>{table?.name ?? 'Tablo seçin'}</h1>
            {table && (
              <div className="meta">
                {total !== null ? `${total} kayıt` : `${rows.length} kayıt`}
                {' · '}{table.columns.length} kolon
                {!editable && ' · salt-okunur'}
              </div>
            )}
          </div>
          {table && editable && (
            <button className="btn btn-primary" onClick={() => setEditing(null)}>Yeni satır</button>
          )}
        </div>

        <div className="content">
          {error && <div className="notice notice-error">{error}</div>}
          {table && !editable && (
            <div className="notice">
              Bu tablo salt-okunur.{' '}
              {!table.canWrite
                ? 'API anahtarının bu tabloya yazma izni yok.'
                : 'Tek kolonlu birincil anahtarı olmadığı için güvenle güncellenemiyor — bileşik anahtarlı bir satırı yanlış eşleştirmek başka bir kaydı değiştirebilirdi.'}
            </div>
          )}

          {loading ? (
            <div className="empty">Yükleniyor…</div>
          ) : !table ? (
            <div className="empty">Soldan bir tablo seçin.</div>
          ) : rows.length === 0 ? (
            <div className="empty">Bu tabloda kayıt yok.</div>
          ) : (
            <>
              <div className="grid-wrap">
                <table>
                  <thead>
                    <tr>
                      {table.columns.map(c => (
                        <th key={c.name}>
                          {c.name}
                          {c.isPK && <span className="col-badge">PK</span>}
                          {c.references && <span className="col-badge">→{c.references.table}</span>}
                        </th>
                      ))}
                      {editable && <th style={{ textAlign: 'right' }}>İşlem</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r, i) => (
                      <tr key={pk ? String(r.values[pk]) : i}>
                        {table.columns.map(c => (
                          <td key={c.name} className={c.isPK ? 'pk-cell' : undefined}>
                            {formatCell(r.values[c.name])}
                          </td>
                        ))}
                        {editable && (
                          <td>
                            <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                              <button className="btn btn-sm" onClick={() => setEditing(r.values)}>Düzenle</button>
                              <button className="btn btn-sm btn-danger" onClick={() => handleDelete(r.values)}>Sil</button>
                            </div>
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="pager">
                <button className="btn btn-sm" disabled={page <= 1}
                        onClick={() => { const p = page - 1; setPage(p); loadRows(table.name, p); }}>Önceki</button>
                <span>{page} / {lastPage}</span>
                <button className="btn btn-sm" disabled={page >= lastPage}
                        onClick={() => { const p = page + 1; setPage(p); loadRows(table.name, p); }}>Sonraki</button>
              </div>
            </>
          )}
        </div>
      </main>

      {table && editing !== undefined && (
        <RowForm
          table={table}
          initial={editing}
          onCancel={() => setEditing(undefined)}
          onSubmit={async values => {
            if (editing === null) {
              await deskApi.create(apiKey, table.name, values);
            } else if (pk) {
              await deskApi.update(apiKey, table.name, pk, String(editing[pk]), values);
            }
            setEditing(undefined);
            await loadRows(table.name, page);
          }}
        />
      )}
    </div>
  );
}

export { DeskApiError };
