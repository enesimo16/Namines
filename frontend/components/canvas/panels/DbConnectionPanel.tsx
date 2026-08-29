'use client';

import React, { useState } from 'react';
import { Database, X, Loader2, Eye, EyeOff, AlertTriangle } from 'lucide-react';
import { API_BASE_URL } from '../../../lib/apiConfig';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { DatabaseSchema } from '../../../types/schema';

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
}

export default function DbConnectionPanel({ isOpen, onClose }: Props) {
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const showToast = useToastStore(s => s.showToast);

  const [dbType, setDbType] = useState('PostgreSQL');
  const [connectionString, setConnectionString] = useState('');
  const [showCs, setShowCs] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
        headers: { 'Content-Type': 'application/json' },
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
    } catch {
      setError('Network error. Check your connection.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-scrim/60 backdrop-blur-sm">
      <div className="bg-surface-800 border border-surface-500 rounded-2xl shadow-2xl w-full max-w-lg mx-4 p-6 flex flex-col gap-5">

        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Database className="w-5 h-5 text-accent-text" />
            <span className="text-content-primary font-semibold text-base">Connect to Live Database</span>
          </div>
          <button onClick={onClose} className="text-content-muted hover:text-content-primary transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Warning */}
        <div className="flex gap-2 bg-surface-600 border border-content-primary/12 rounded-xl p-3 text-content-secondary text-xs">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          <span>
            Your connection string is sent to the Namines API and never stored. Use a read-only DB user when possible.
          </span>
        </div>

        {/* DB Type */}
        <div className="flex flex-col gap-1.5">
          <label className="text-content-secondary text-sm font-medium">Database engine</label>
          <div className="flex gap-2 flex-wrap">
            {DB_TYPES.map(t => (
              <button
                key={t.value}
                onClick={() => { setDbType(t.value); setConnectionString(''); setError(null); }}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium border transition-all ${
                  dbType === t.value
                    ? 'bg-content-primary/30 border-accent-hover/60 text-surface-900'
                    : 'bg-surface-700 border-surface-500 text-content-muted hover:border-accent-hover/50 hover:text-content-secondary'
                }`}
              >
                {t.label}
              </button>
            ))}
          </div>
        </div>

        {/* Connection String */}
        <div className="flex flex-col gap-1.5">
          <label className="text-content-secondary text-sm font-medium">Connection string</label>
          <div className="relative">
            <textarea
              value={connectionString}
              onChange={e => { setConnectionString(e.target.value); setError(null); }}
              placeholder={PLACEHOLDERS[dbType] ?? ''}
              rows={3}
              className="w-full bg-surface-900 border border-surface-500 focus:border-accent-hover rounded-xl px-3 py-2.5 text-sm text-content-primary placeholder-content-muted outline-none resize-none font-mono"
              style={{ WebkitTextSecurity: showCs ? 'none' : 'disc' } as React.CSSProperties}
            />
            <button
              type="button"
              onClick={() => setShowCs(v => !v)}
              className="absolute right-2.5 top-2.5 text-content-muted hover:text-content-primary transition-colors"
              title={showCs ? 'Hide' : 'Show'}
            >
              {showCs ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
            </button>
          </div>
        </div>

        {/* Error */}
        {error && (
          <div className="bg-danger-subtle/20 border border-danger/30 rounded-xl p-3 text-danger-text text-xs">
            {error}
          </div>
        )}

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <button
            onClick={onClose}
            className="px-4 py-2 rounded-xl bg-surface-700 hover:bg-surface-600 text-content-secondary text-sm font-medium border border-surface-500 transition-all"
          >
            Cancel
          </button>
          <button
            onClick={handleIntrospect}
            disabled={loading || !connectionString.trim()}
            className="flex items-center gap-2 px-5 py-2 rounded-xl bg-content-primary hover:bg-content-secondary disabled:opacity-50 disabled:cursor-not-allowed text-surface-900 text-sm font-semibold border border-content-primary/[0.04]0 transition-all"
          >
            {loading && <Loader2 className="w-4 h-4 animate-spin" />}
            {loading ? 'Connecting…' : 'Import Schema'}
          </button>
        </div>
      </div>
    </div>
  );
}
