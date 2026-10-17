'use client';

import React, { useState } from 'react';
import { Database, X, Loader2, Eye, EyeOff, AlertTriangle, ChevronLeft, ChevronRight, ArrowLeft } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { gatewayService } from '../../../services/api';
import { GatewayRow } from '../../../types/gateway';
import { errorMessage } from '../../../lib/errors';
import { useEscapeKey } from '../../../hooks/useEscapeKey';
import { Select } from '../../workspace/WorkspaceKit';

const DB_TYPES = [
  { value: 'MSSQL',      label: 'SQL Server' },
  { value: 'PostgreSQL', label: 'PostgreSQL' },
  { value: 'MySQL',      label: 'MySQL' },
  { value: 'MariaDB',    label: 'MariaDB' },
  { value: 'Oracle',     label: 'Oracle' },
];

const PLACEHOLDERS: Record<string, string> = {
  MSSQL:      'Server=myhost,1433;Database=mydb;User Id=sa;Password=***;TrustServerCertificate=True',
  PostgreSQL: 'Host=myhost;Port=5432;Database=mydb;Username=postgres;Password=***',
  MySQL:      'Server=myhost;Port=3306;Database=mydb;Uid=root;Pwd=***',
  MariaDB:    'Server=myhost;Port=3306;Database=mydb;Uid=root;Pwd=***',
  Oracle:     'Data Source=myhost:1521/ORCL;User Id=system;Password=***',
};

const PAGE_SIZE = 25;

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

/**
 * G14 — Minimal Gateway. Şemadaki herhangi bir tabloyu, kullanıcının verdiği canlı
 * bağlantı üzerinden, salt-okunur şekilde sayfalı liste + tek satır detay olarak
 * gösterir. Yazma yolu YOK — sadece backend'in ürettiği SELECT'ler. Connection
 * string DbConnectionPanel ile aynı gerekçeyle hiçbir yerde saklanmaz.
 */
export default function GatewayExplorerPanel({ isOpen, onClose }: Props) {
  const schema = useSchemaStore(s => s.schema);
  const showToast = useToastStore(s => s.showToast);

  const [dbType, setDbType] = useState('PostgreSQL');
  const [connectionString, setConnectionString] = useState('');
  const [showCs, setShowCs] = useState(false);
  const [selectedTable, setSelectedTable] = useState<string>('');
  const [connected, setConnected] = useState(false);

  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<GatewayRow[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [detailRow, setDetailRow] = useState<GatewayRow | null>(null);
  const [isDetailLoading, setIsDetailLoading] = useState(false);

  useEscapeKey(isOpen, onClose);

  if (!isOpen) return null;

  const table = schema?.tables.find(t => t.name === selectedTable) ?? null;
  const pkColumn = table?.columns.find(c => c.isPK)?.name ?? null;

  const reset = () => {
    setConnected(false);
    setRows([]);
    setTotalCount(0);
    setPage(1);
    setError(null);
    setDetailRow(null);
  };

  const loadPage = async (nextPage: number) => {
    if (!selectedTable || !connectionString.trim()) return;
    setIsLoading(true);
    setError(null);
    try {
      // PK varsa sıralama kolonu olarak gönderilir — ORDER BY olmadan sayfalar arası
      // satır sırası garanti değil (aynı satır iki sayfada çıkabilir). Toplam sayım
      // yalnızca ilk yüklemede istenir; sayfa gezinmesinde COUNT(*) tekrar edilmez.
      const isFirstLoad = nextPage === 1;
      const result = await gatewayService.list(
        connectionString.trim(), dbType, selectedTable, nextPage, PAGE_SIZE,
        pkColumn, isFirstLoad,
      );
      setRows(result.rows);
      if (result.totalCount >= 0) setTotalCount(result.totalCount);
      setPage(result.page);
      setConnected(true);
    } catch (err) {
      setError(errorMessage(err, 'Failed to load data.'));
      setConnected(false);
    } finally {
      setIsLoading(false);
    }
  };

  const openDetail = async (row: GatewayRow) => {
    if (!pkColumn) return;
    const pkValue = row.values[pkColumn];
    if (pkValue === null || pkValue === undefined) return;

    setIsDetailLoading(true);
    try {
      const detail = await gatewayService.detail(connectionString.trim(), dbType, selectedTable, pkColumn, String(pkValue));
      setDetailRow(detail);
    } catch {
      showToast('Failed to load row detail.', 'error');
    } finally {
      setIsDetailLoading(false);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const columns = rows.length > 0 ? Object.keys(rows[0].values) : (table?.columns.map(c => c.name) ?? []);

  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-scrim/70 backdrop-blur-sm p-4 animate-in fade-in duration-200 select-none">
      <div className="bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] shadow-2xl w-full max-w-3xl max-h-[85vh] flex flex-col overflow-hidden">

        {/* Header */}
        <div className="shrink-0 flex items-center justify-between px-5 py-3.5 border-b border-line bg-surface-800/40">
          <div className="flex items-center gap-2.5">
            <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong flex items-center justify-center text-content-primary">
              <Database className="w-3.5 h-3.5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-bold text-content-primary">Data Explorer</span>
                {connected && (
                  <span className="text-micro px-2 py-0.5 rounded-full bg-surface-700 border border-line text-content-primary font-mono font-bold">
                    {selectedTable}
                  </span>
                )}
              </div>
              <p className="text-micro text-content-muted">Read-only live database record gateway</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto p-5">
          {!connected ? (
            <div className="flex flex-col gap-4">
              <div className="flex gap-2.5 bg-surface-800/80 border border-line rounded-[var(--radius-card)] p-3 text-content-muted text-xs">
                <AlertTriangle className="w-4 h-4 shrink-0 text-content-muted mt-0.5" />
                <span className="leading-relaxed">Read-only live inspection. Only parameterized SELECT queries run — your connection string is never stored.</span>
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-micro font-bold uppercase tracking-wider text-content-muted">Database Engine</label>
                <div className="flex gap-1.5 flex-wrap">
                  {DB_TYPES.map(t => (
                    <button
                      key={t.value}
                      type="button"
                      onClick={() => { setDbType(t.value); setConnectionString(''); setError(null); }}
                      className={`px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold border transition-all cursor-pointer ${
                        dbType === t.value
                          ? 'bg-surface-700 border-line-strong text-content-primary shadow-xs'
                          : 'bg-surface-800/60 border-line text-content-muted hover:text-content-primary hover:bg-surface-700/50'
                      }`}
                    >
                      {t.label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-micro font-bold uppercase tracking-wider text-content-muted">Connection String</label>
                <div className="relative">
                  <textarea
                    aria-label="Connection string"
                    value={connectionString}
                    onChange={e => { setConnectionString(e.target.value); setError(null); }}
                    placeholder={PLACEHOLDERS[dbType] ?? ''}
                    rows={2}
                    className="w-full bg-surface-800/90 border border-line focus:border-line-strong rounded-[var(--radius-control)] px-3 py-2 text-xs text-content-primary placeholder-content-subtle outline-none resize-none font-mono leading-relaxed"
                    style={{ WebkitTextSecurity: showCs ? 'none' : 'disc' } as React.CSSProperties}
                  />
                  <button
                    type="button"
                    onClick={() => setShowCs(v => !v)}
                    className="absolute right-2.5 top-2 p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700/60 transition-colors cursor-pointer"
                    aria-label={showCs ? 'Hide connection string' : 'Show connection string'}
                  >
                    {showCs ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                  </button>
                </div>
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-micro font-bold uppercase tracking-wider text-content-muted">Table to Inspect</label>
                <Select
                  aria-label="Table"
                  value={selectedTable}
                  onChange={e => setSelectedTable(e.target.value)}
                >
                  <option value="">Select a table from the schema…</option>
                  {schema?.tables.map(t => (
                    <option key={t.id} value={t.name}>{t.name}</option>
                  ))}
                </Select>
                {selectedTable && !pkColumn && (
                  <span className="text-micro text-content-muted flex items-center gap-1.5 mt-0.5">
                    <AlertTriangle className="w-3.5 h-3.5 shrink-0 text-content-muted" /> No primary key — row detail is unavailable and order is not guaranteed.
                  </span>
                )}
              </div>

              {error && (
                <div className="bg-danger-subtle border border-danger/30 rounded-[var(--radius-control)] p-2.5 text-danger-text text-xs leading-relaxed">
                  {error}
                </div>
              )}

              <div className="flex justify-end gap-2 pt-2 border-t border-line">
                <button
                  onClick={onClose}
                  className="px-3.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-secondary hover:text-content-primary text-xs font-semibold border border-line transition-all cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  onClick={() => loadPage(1)}
                  disabled={isLoading || !connectionString.trim() || !selectedTable}
                  className="flex items-center gap-2 px-4 py-1.5 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-secondary disabled:opacity-50 disabled:cursor-not-allowed text-surface-900 text-xs font-bold transition-all shadow-sm cursor-pointer"
                >
                  {isLoading && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  <span>{isLoading ? 'Connecting…' : 'Browse Data'}</span>
                </button>
              </div>
            </div>
          ) : detailRow ? (
            <div className="flex flex-col gap-3">
              <button
                onClick={() => setDetailRow(null)}
                className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line text-xs font-semibold text-content-primary transition-colors cursor-pointer w-fit"
              >
                <ArrowLeft className="w-3.5 h-3.5" /> Back to Table Records
              </button>
              <div className="bg-surface-800/80 border border-line rounded-[var(--radius-card)] divide-y divide-line overflow-hidden">
                {Object.entries(detailRow.values).map(([key, value]) => (
                  <div key={key} className="flex items-start gap-4 px-4 py-2.5">
                    <span className="w-44 shrink-0 text-xs font-mono font-bold text-content-primary">{key}</span>
                    <span className="text-xs text-content-secondary font-mono break-all leading-relaxed">
                      {value === null ? <span className="text-content-subtle italic text-[10px] px-1 py-0.5 rounded-[var(--radius-control)] bg-surface-700/60">NULL</span> : String(value)}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between pb-1 border-b border-line">
                <button
                  onClick={reset}
                  className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line text-xs font-semibold text-content-primary transition-colors cursor-pointer"
                >
                  <ArrowLeft className="w-3.5 h-3.5" /> Change connection
                </button>
                <span className="text-micro text-content-muted font-mono font-semibold">{totalCount} records</span>
              </div>

              <div className="overflow-x-auto border border-line rounded-[var(--radius-card)] relative bg-surface-800/60">
                {isLoading && (
                  <div className="absolute inset-0 bg-surface-900/70 backdrop-blur-sm flex items-center justify-center z-10">
                    <Loader2 className="w-5 h-5 text-content-primary animate-spin" />
                  </div>
                )}
                <table className="w-full text-left border-collapse text-xs">
                  <thead>
                    <tr className="bg-surface-700/80 border-b border-line text-content-muted uppercase tracking-wide font-bold text-[10px]">
                      {columns.map(c => <th key={c} className="py-2.5 px-3 font-mono">{c}</th>)}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line font-mono text-micro text-content-secondary">
                    {rows.length === 0 ? (
                      <tr><td colSpan={columns.length || 1} className="py-8 text-center text-content-muted">No rows found in this table.</td></tr>
                    ) : rows.map((row, i) => (
                      <tr
                        key={i}
                        onClick={() => pkColumn && openDetail(row)}
                        className={`hover:bg-surface-700/40 transition-colors ${pkColumn ? 'cursor-pointer' : ''}`}
                      >
                        {columns.map(c => (
                          <td key={c} className="py-2 px-3 max-w-[200px] truncate">
                            {row.values[c] === null ? (
                              <span className="text-content-subtle italic text-[10px] px-1 py-0.5 rounded-[var(--radius-control)] bg-surface-700/60">NULL</span>
                            ) : (
                              String(row.values[c])
                            )}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {isDetailLoading && (
                <div className="flex items-center gap-2 text-micro text-content-muted">
                  <Loader2 className="w-3.5 h-3.5 animate-spin text-content-primary" /> Loading row detail…
                </div>
              )}

              <div className="flex items-center justify-between pt-1">
                <span className="text-micro text-content-muted font-medium">Page {page} of {totalPages}</span>
                <div className="flex items-center gap-1.5">
                  <button
                    onClick={() => loadPage(page - 1)}
                    disabled={page <= 1 || isLoading}
                    className="p-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line disabled:opacity-40 disabled:cursor-not-allowed text-content-secondary hover:text-content-primary transition-all cursor-pointer"
                    aria-label="Previous page"
                  >
                    <ChevronLeft className="w-3.5 h-3.5" />
                  </button>
                  <button
                    onClick={() => loadPage(page + 1)}
                    disabled={page >= totalPages || isLoading}
                    className="p-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line disabled:opacity-40 disabled:cursor-not-allowed text-content-secondary hover:text-content-primary transition-all cursor-pointer"
                    aria-label="Next page"
                  >
                    <ChevronRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
