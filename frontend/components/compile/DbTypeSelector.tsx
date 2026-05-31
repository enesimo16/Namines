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
      <Database className="w-4 h-4 text-zinc-500 shrink-0" />
      <select
        value={selectedDb}
        onChange={(e) => onSelect(e.target.value)}
        disabled={disabled}
        className="bg-zinc-900 border border-zinc-700 text-zinc-200 text-sm rounded-lg px-3 py-2 focus:outline-none focus:border-indigo-500 transition-colors cursor-pointer hover:bg-zinc-800 disabled:opacity-50 disabled:cursor-not-allowed"
        aria-label="Veritabanı türü seç"
      >
        {DB_OPTIONS.map((opt) => (
          <option key={opt.id} value={opt.id}>{opt.label}</option>
        ))}
      </select>
    </div>
  );
}
