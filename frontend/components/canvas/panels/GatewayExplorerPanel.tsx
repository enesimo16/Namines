'use client';

import React, { useState } from 'react';
import { Database, X, Loader2, Eye, EyeOff, AlertTriangle, ChevronLeft, ChevronRight, ArrowLeft } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { gatewayService } from '../../../services/api';
import { GatewayRow } from '../../../types/gateway';

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
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Failed to load data.');
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
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-scrim/60 backdrop-blur-sm p-4">
      <div className="bg-surface-700 border border-surface-500 rounded-[var(--radius-card)] shadow-2xl w-full max-w-3xl max-h-[85vh] flex flex-col overflow-hidden">

        {/* Header */}
        <div className="shrink-0 flex items-center justify-between px-4 py-3 border-b border-surface-500">
          <div className="flex items-center gap-2">
            <Database className="w-4 h-4 text-accent-text" />
            <span className="text-[13px] font-semibold text-content-primary">Data Explorer</span>
            {connected && <span className="text-[10px] text-content-subtle font-mono">— {selectedTable}</span>}
          </div>
          <button onClick={onClose} className="text-content-subtle hover:text-content-primary transition-colors cursor-pointer" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto p-4">
          {!connected ? (
            <div className="flex flex-col gap-4">
              <div className="flex gap-2 bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] p-3 text-content-secondary text-[11px]">
                <AlertTriangle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
                <span>Read-only. Only SELECT queries run — your connection string is sent to the API and never stored. Use a read-only DB user when possible.</span>
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-[11px] font-semibold text-content-subtle">Database engine</label>
                <div className="flex gap-1.5 flex-wrap">
                  {DB_TYPES.map(t => (
                    <button
                      key={t.value}
                      onClick={() => { setDbType(t.value); setConnectionString(''); setError(null); }}
                      className={`px-2.5 py-1 rounded-[var(--radius-control)] text-[11px] font-medium border transition-all cursor-pointer ${
                        dbType === t.value
                          ? 'bg-accent-subtle border-accent-hover/60 text-accent-text'
                          : 'bg-surface-600 border-surface-500 text-content-subtle hover:text-content-secondary'
                      }`}
                    >
                      {t.label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-[11px] font-semibold text-content-subtle">Connection string</label>
                <div className="relative">
                  <textarea
                    value={connectionString}
                    onChange={e => { setConnectionString(e.target.value); setError(null); }}
                    placeholder={PLACEHOLDERS[dbType] ?? ''}
                    rows={2}
                    className="w-full bg-surface-800 border border-surface-500 focus:border-accent-hover rounded-[var(--radius-control)] px-3 py-2 text-[12px] text-content-primary placeholder-content-subtle outline-none resize-none font-mono"
                    style={{ WebkitTextSecurity: showCs ? 'none' : 'disc' } as React.CSSProperties}
                  />
                  <button
                    type="button"
                    onClick={() => setShowCs(v => !v)}
                    className="absolute right-2.5 top-2 text-content-subtle hover:text-content-primary transition-colors cursor-pointer"
                    aria-label={showCs ? 'Hide connection string' : 'Show connection string'}
                  >
                    {showCs ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                  </button>
                </div>
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-[11px] font-semibold text-content-subtle">Table</label>
                <select
                  value={selectedTable}
                  onChange={e => setSelectedTable(e.target.value)}
                  className="bg-surface-800 border border-surface-500 focus:border-accent-hover rounded-[var(--radius-control)] px-3 py-2 text-[12px] text-content-secondary outline-none cursor-pointer"
                >
                  <option value="">Select a table from the schema…</option>
                  {schema?.tables.map(t => (
                    <option key={t.id} value={t.name}>{t.name}</option>
                  ))}
                </select>
                {selectedTable && !pkColumn && (
                  <span className="text-[10px] text-content-secondary flex items-center gap-1">
                    <AlertTriangle className="w-3 h-3 shrink-0" /> No primary key — row detail is unavailable and page order isn&apos;t guaranteed stable.
                  </span>
                )}
              </div>

              {error && (
                <div className="bg-danger-subtle border border-danger/30 rounded-[var(--radius-control)] p-2.5 text-danger-text text-[11px]">
                  {error}
                </div>
              )}

              <div className="flex justify-end gap-2">
                <button
                  onClick={onClose}
                  className="px-3.5 py-2 rounded-[var(--radius-control)] bg-white/[0.06] hover:bg-white/[0.1] text-content-secondary text-[12px] font-medium transition-all cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  onClick={() => loadPage(1)}
                  disabled={isLoading || !connectionString.trim() || !selectedTable}
                  className="flex items-center gap-2 px-4 py-2 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-primary-hover disabled:opacity-50 disabled:cursor-not-allowed text-surface-900 text-[12px] font-semibold transition-all cursor-pointer"
                >
                  {isLoading && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  {isLoading ? 'Connecting…' : 'Browse Data'}
                </button>
              </div>
            </div>
          ) : detailRow ? (
            <div className="flex flex-col gap-3">
              <button
                onClick={() => setDetailRow(null)}
                className="flex items-center gap-1.5 text-[11px] font-semibold text-content-subtle hover:text-content-primary transition-colors cursor-pointer w-fit"
              >
                <ArrowLeft className="w-3.5 h-3.5" /> Back to list
              </button>
              <div className="bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] divide-y divide-surface-500">
                {Object.entries(detailRow.values).map(([key, value]) => (
                  <div key={key} className="flex items-start gap-4 px-3.5 py-2">
                    <span className="w-40 shrink-0 text-[11px] font-mono font-semibold text-accent-text">{key}</span>
                    <span className="text-[12px] text-content-secondary font-mono break-all">{value === null ? <em className="text-content-subtle">NULL</em> : String(value)}</span>
                  </div>
                ))}
              </div>
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <button
                  onClick={reset}
                  className="flex items-center gap-1.5 text-[11px] font-semibold text-content-subtle hover:text-content-primary transition-colors cursor-pointer"
                >
                  <ArrowLeft className="w-3.5 h-3.5" /> Change connection
                </button>
                <span className="text-[10px] text-content-subtle font-mono">{totalCount} rows</span>
              </div>

              <div className="overflow-x-auto border border-surface-500 rounded-[var(--radius-control)] relative">
                {isLoading && (
                  <div className="absolute inset-0 bg-surface-800/70 backdrop-blur-sm flex items-center justify-center z-10">
                    <Loader2 className="w-5 h-5 text-content-muted animate-spin" />
                  </div>
                )}
                <table className="w-full text-left border-collapse text-[11px]">
                  <thead>
                    <tr className="bg-surface-600 border-b border-surface-500 text-content-subtle uppercase tracking-wide font-bold">
                      {columns.map(c => <th key={c} className="py-2 px-3 font-mono">{c}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {rows.length === 0 ? (
                      <tr><td colSpan={columns.length || 1} className="py-6 text-center text-content-subtle">No rows.</td></tr>
                    ) : rows.map((row, i) => (
                      <tr
                        key={i}
                        onClick={() => pkColumn && openDetail(row)}
                        className={`border-b border-surface-500 text-content-secondary font-mono ${pkColumn ? 'hover:bg-white/[0.04] cursor-pointer' : ''}`}
                      >
                        {columns.map(c => (
                          <td key={c} className="py-1.5 px-3 max-w-[180px] truncate">
                            {row.values[c] === null ? <em className="text-content-subtle">NULL</em> : String(row.values[c])}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {isDetailLoading && (
                <div className="flex items-center gap-2 text-[11px] text-content-subtle">
                  <Loader2 className="w-3 h-3 animate-spin" /> Loading row…
                </div>
              )}

              <div className="flex items-center justify-between">
                <span className="text-[10px] text-content-subtle">Page {page} of {totalPages}</span>
                <div className="flex items-center gap-1.5">
                  <button
                    onClick={() => loadPage(page - 1)}
                    disabled={page <= 1 || isLoading}
                    className="p-1.5 rounded-[var(--radius-control)] bg-white/[0.06] hover:bg-white/[0.1] disabled:opacity-40 disabled:cursor-not-allowed text-content-secondary transition-all cursor-pointer"
                    aria-label="Previous page"
                  >
                    <ChevronLeft className="w-3.5 h-3.5" />
                  </button>
                  <button
                    onClick={() => loadPage(page + 1)}
                    disabled={page >= totalPages || isLoading}
                    className="p-1.5 rounded-[var(--radius-control)] bg-white/[0.06] hover:bg-white/[0.1] disabled:opacity-40 disabled:cursor-not-allowed text-content-secondary transition-all cursor-pointer"
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
