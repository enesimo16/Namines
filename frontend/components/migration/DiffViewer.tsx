import React from 'react';
import { SchemaDiffResult } from '../../types/migration';
import { PlusCircle, MinusCircle, AlertTriangle, CheckCircle } from 'lucide-react';

interface DiffViewerProps {
  diff: SchemaDiffResult;
}

export default function DiffViewer({ diff }: DiffViewerProps) {
  const hasAdded = diff?.addedTables && diff.addedTables.length > 0;
  const hasRemoved = diff?.removedTables && diff.removedTables.length > 0;
  const hasModified = diff?.modifiedTables && diff.modifiedTables.length > 0;
  const hasChanges = hasAdded || hasRemoved || hasModified;

  if (!hasChanges) {
    return (
      <div className="flex flex-col items-center justify-center py-10 text-content-muted">
        <CheckCircle className="w-12 h-12 text-success-text mb-2" />
        <span className="text-[14px]">No changes were found between schemas.</span>
      </div>
    );
  }

  return (
    <div className="space-y-4 max-h-[350px] overflow-y-auto pr-2 custom-scrollbar">
      {/* Added Tables */}
      {hasAdded && (
        <div className="bg-success-subtle border border-success/20 rounded-[var(--radius-card)] p-4">
          <h4 className="text-success-text text-sm font-bold flex items-center gap-2 mb-2">
            <PlusCircle className="w-4 h-4" />
            <span>Added Tables ({diff.addedTables.length})</span>
          </h4>
          <ul className="grid grid-cols-2 gap-2">
            {diff.addedTables.map((t) => (
              <li key={t} className="text-content-secondary text-xs bg-success-subtle border border-success/20 px-2.5 py-1.5 rounded-[var(--radius-control)] flex items-center gap-1.5 font-medium">
                <span className="w-1.5 h-1.5 rounded-full bg-success" />
                {t}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Deleted Tables */}
      {hasRemoved && (
        <div className="bg-danger-subtle border border-danger/20 rounded-[var(--radius-card)] p-4">
          <h4 className="text-danger-text text-sm font-bold flex items-center gap-2 mb-2">
            <MinusCircle className="w-4 h-4" />
            <span>Deleted Tables ({diff.removedTables.length})</span>
          </h4>
          <ul className="grid grid-cols-2 gap-2">
            {diff.removedTables.map((t) => (
              <li key={t} className="text-content-secondary text-xs bg-danger-subtle border border-danger/20 px-2.5 py-1.5 rounded-[var(--radius-control)] flex items-center gap-1.5 font-medium line-through decoration-danger/50">
                <span className="w-1.5 h-1.5 rounded-full bg-danger" />
                {t}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Modified Tables */}
      {hasModified && (
        <div className="bg-surface-600 border border-content-primary/15 rounded-[var(--radius-card)] p-4">
          <h4 className="text-content-secondary text-sm font-bold flex items-center gap-2 mb-2">
            <AlertTriangle className="w-4 h-4" />
            <span>Modified Tables ({diff.modifiedTables.length})</span>
          </h4>
          <div className="space-y-3">
            {diff.modifiedTables.map((tbl) => (
              <div key={tbl.tableName} className="bg-surface-700 border border-content-primary/8 rounded-[var(--radius-control)] p-3 space-y-2">
                <div className="text-content-primary text-xs font-bold border-b border-content-primary/8 pb-1.5 flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 rounded-full bg-content-muted" />
                  {tbl.tableName}
                </div>
                
                {tbl.addedColumns.length > 0 && (
                  <div className="text-[11px] text-content-muted">
                    <span className="text-success-text font-bold mr-1">Added:</span>
                    {tbl.addedColumns.join(', ')}
                  </div>
                )}
                
                {tbl.removedColumns.length > 0 && (
                  <div className="text-[11px] text-content-muted">
                    <span className="text-danger-text font-bold mr-1 line-through">Deleted:</span>
                    <span className="line-through decoration-danger/30">{tbl.removedColumns.join(', ')}</span>
                  </div>
                )}

                {tbl.modifiedColumns.length > 0 && (
                  <div className="text-[11px] text-content-muted">
                    <span className="text-content-secondary font-bold mr-1">Modified:</span>
                    {tbl.modifiedColumns.join(', ')}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
