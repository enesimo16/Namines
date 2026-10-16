'use client';
import { csrfHeaders } from '@/lib/csrf';

import React, { useState } from 'react';
import { Database, X, Loader2, Eye, EyeOff, AlertTriangle } from 'lucide-react';
import { API_BASE_URL } from '../../../lib/apiConfig';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { DatabaseSchema } from '../../../types/schema';
import { useEscapeKey } from '../../../hooks/useEscapeKey';

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

interface Props {
  isOpen: boolean;
  onClose: () => void;
  /**
   * Sema GERCEKTEN uygulandiginda cagrilir (iptal edildiginde DEGIL).
   *
   * Neden `onClose`'tan ayri: `/new` ekraninda bu panel "prompt yazmadan
   * basla" yolunun bir adimi ve sema geldiginde canvas'a gecilmesi gerekiyor.
   * `onClose` iptal ederken de calisiyor; ona baglanmak, kullanici vazgectiginde
   * de canvas'a atmak demekti.
   */
  onApplied?: () => void;
}

export default function DbConnectionPanel({ isOpen, onClose, onApplied }: Props) {
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const showToast = useToastStore(s => s.showToast);

  const [dbType, setDbType] = useState('PostgreSQL');
  const [connectionString, setConnectionString] = useState('');
  const [showCs, setShowCs] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEscapeKey(isOpen, onClose);

  if (!isOpen) return null;

  const handleIntrospect = async () => {
    if (!connectionString.trim()) {
      setError('Connection string is required.');
      return;
    }
    setError(null);
    setLoading(true);
    try {
      const res = await fetch(`${API_BASE_URL}/dbintrospect`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', ...csrfHeaders },
        body: JSON.stringify({ connectionString: connectionString.trim(), dbType }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({ message: 'Unknown error.' }));
        setError(body.message ?? 'Failed to connect.');
        return;
      }

      const schema: DatabaseSchema = await res.json();
      loadFromSchema(schema);
      showToast(`Imported ${schema.tables?.length ?? 0} tables from ${dbType}.`, 'success');
      onClose();
      onApplied?.();
    } catch {
      setError('Network error. Check your connection.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200 select-none">
      <div className="bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] shadow-2xl w-full max-w-lg mx-4 flex flex-col overflow-hidden">

        {/* Header */}
        <div className="flex items-center justify-between px-5 py-3.5 border-b border-line bg-surface-800/40">
          <div className="flex items-center gap-2.5">
            <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong flex items-center justify-center text-content-primary">
              <Database className="w-3.5 h-3.5" />
            </div>
            <div>
              <span className="text-sm font-bold text-content-primary">Connect Live Database</span>
              <p className="text-micro text-content-muted">Introspect tables directly into canvas</p>
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

        <div className="p-5 flex flex-col gap-4 overflow-y-auto">
          {/* Security Note Callout */}
          <div className="flex gap-2.5 bg-surface-800/80 border border-line rounded-[var(--radius-card)] p-3 text-content-muted text-xs">
            <AlertTriangle className="w-4 h-4 shrink-0 text-content-muted mt-0.5" />
            <span className="leading-relaxed">
              Your connection string is processed in-memory and never stored. We recommend using a read-only database user.
            </span>
          </div>

          {/* DB Type Selector */}
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

          {/* Connection String Input */}
          <div className="flex flex-col gap-1.5">
            <label className="text-micro font-bold uppercase tracking-wider text-content-muted">Connection String</label>
            <div className="relative">
              <textarea
                aria-label="Connection string"
                value={connectionString}
                onChange={e => { setConnectionString(e.target.value); setError(null); }}
                placeholder={PLACEHOLDERS[dbType] ?? ''}
                rows={3}
                className="w-full bg-surface-800/90 border border-line focus:border-line-strong rounded-[var(--radius-control)] px-3 py-2.5 text-xs text-content-primary placeholder-content-subtle outline-none resize-none font-mono leading-relaxed"
                style={{ WebkitTextSecurity: showCs ? 'none' : 'disc' } as React.CSSProperties}
              />
              <button
                type="button"
                onClick={() => setShowCs(v => !v)}
                className="absolute right-2.5 top-2.5 p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700/60 transition-colors cursor-pointer"
                title={showCs ? 'Hide' : 'Show'}
              >
                {showCs ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
              </button>
            </div>
          </div>

          {/* Error Message */}
          {error && (
            <div className="bg-danger-subtle border border-danger/30 rounded-[var(--radius-control)] p-2.5 text-danger-text text-xs leading-relaxed">
              {error}
            </div>
          )}
        </div>

        {/* Action Footer */}
        <div className="flex items-center justify-end gap-2 px-5 py-3 border-t border-line bg-surface-800/40 shrink-0">
          <button
            onClick={onClose}
            className="px-3.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-secondary hover:text-content-primary text-xs font-semibold border border-line transition-all cursor-pointer"
          >
            Cancel
          </button>
          <button
            onClick={handleIntrospect}
            disabled={loading || !connectionString.trim()}
            className="flex items-center gap-2 px-4 py-1.5 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-secondary disabled:opacity-50 disabled:cursor-not-allowed text-surface-900 text-xs font-bold transition-all shadow-sm cursor-pointer"
          >
            {loading && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            <span>{loading ? 'Connecting…' : 'Import Schema'}</span>
          </button>
        </div>
      </div>
    </div>
  );
}
