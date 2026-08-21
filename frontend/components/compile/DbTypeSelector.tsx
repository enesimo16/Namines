import React from 'react';
import { Database } from 'lucide-react';
import { DbType } from '../../store/useSchemaStore';

interface DbTypeSelectorProps {
  selectedDb: string;
  onSelect: (db: string) => void;
  disabled?: boolean;
}

const DB_OPTIONS: { id: DbType; label: string }[] = [
  { id: 'MSSQL',      label: 'SQL Server' },
  { id: 'PostgreSQL', label: 'PostgreSQL' },
  { id: 'MySQL',      label: 'MySQL'      },
  { id: 'SQLite',     label: 'SQLite'     },
  { id: 'Oracle',     label: 'Oracle'     },
  { id: 'MariaDB',    label: 'MariaDB'    },
  { id: 'Db2',        label: 'IBM Db2'    },
  { id: 'Firebird',   label: 'Firebird'   },
  { id: 'Spanner',    label: 'Google Spanner' },
  { id: 'Redshift',   label: 'Amazon Redshift' },
];

export default function DbTypeSelector({ selectedDb, onSelect, disabled }: DbTypeSelectorProps) {
  return (
    <div className="flex items-center gap-2">
      <Database className="w-3.5 h-3.5 text-content-muted shrink-0" />
      <select
        value={selectedDb}
        onChange={(e) => onSelect(e.target.value)}
        disabled={disabled}
        className="bg-surface-600 border border-surface-500 text-content-secondary text-xs rounded-lg px-3 py-1.5 focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] transition-colors cursor-pointer hover:bg-surface-500/30 disabled:opacity-50 disabled:cursor-not-allowed"
        aria-label="Select database type"
      >
        {DB_OPTIONS.map((opt) => (
          <option key={opt.id} value={opt.id}>{opt.label}</option>
        ))}
      </select>
    </div>
  );
}
