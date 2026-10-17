import React from 'react';
import { Database } from 'lucide-react';
import { DbType } from '../../store/useSchemaStore';
import Select from '../ui/Select';

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
];

export default function DbTypeSelector({ selectedDb, onSelect, disabled }: DbTypeSelectorProps) {
  return (
    <div className="flex items-center gap-2 w-full">
      <Database className="w-3.5 h-3.5 text-content-muted shrink-0" />
      <Select
        value={selectedDb}
        onChange={(e) => onSelect(e.target.value)}
        disabled={disabled}
        placement="top"
        className="flex-1 min-w-0"
        aria-label="Select database type"
      >
        {DB_OPTIONS.map((opt) => (
          <option key={opt.id} value={opt.id}>{opt.label}</option>
        ))}
      </Select>
    </div>
  );
}
