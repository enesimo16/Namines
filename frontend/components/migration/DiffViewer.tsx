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
      <div className="flex flex-col items-center justify-center py-10 text-zinc-400">
        <CheckCircle className="w-12 h-12 text-emerald-500 mb-2 animate-bounce" />
        <span className="text-[14px]">Şemalar arasında herhangi bir değişiklik bulunamadı.</span>
      </div>
    );
  }

  return (
    <div className="space-y-4 max-h-[350px] overflow-y-auto pr-2 custom-scrollbar">
      {/* Eklenen Tablolar */}
      {hasAdded && (
        <div className="bg-emerald-950/20 border border-emerald-500/20 rounded-xl p-4">
          <h4 className="text-emerald-400 text-sm font-bold flex items-center gap-2 mb-2">
            <PlusCircle className="w-4 h-4" />
            <span>Eklenen Tablolar ({diff.addedTables.length})</span>
          </h4>
          <ul className="grid grid-cols-2 gap-2">
            {diff.addedTables.map((t) => (
              <li key={t} className="text-zinc-300 text-xs bg-emerald-500/10 border border-emerald-500/20 px-2.5 py-1.5 rounded-lg flex items-center gap-1.5 font-medium">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
                {t}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Silinen Tablolar */}
      {hasRemoved && (
        <div className="bg-rose-950/20 border border-rose-500/20 rounded-xl p-4">
          <h4 className="text-rose-400 text-sm font-bold flex items-center gap-2 mb-2">
            <MinusCircle className="w-4 h-4" />
            <span>Silinen Tablolar ({diff.removedTables.length})</span>
          </h4>
          <ul className="grid grid-cols-2 gap-2">
            {diff.removedTables.map((t) => (
              <li key={t} className="text-zinc-300 text-xs bg-rose-500/10 border border-rose-500/20 px-2.5 py-1.5 rounded-lg flex items-center gap-1.5 font-medium line-through decoration-rose-500/50">
                <span className="w-1.5 h-1.5 rounded-full bg-rose-500" />
                {t}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Değişen Tablolar */}
      {hasModified && (
        <div className="bg-amber-950/20 border border-amber-500/20 rounded-xl p-4">
          <h4 className="text-amber-400 text-sm font-bold flex items-center gap-2 mb-2">
            <AlertTriangle className="w-4 h-4" />
            <span>Değişen Tablolar ({diff.modifiedTables.length})</span>
          </h4>
          <div className="space-y-3">
            {diff.modifiedTables.map((tbl) => (
              <div key={tbl.tableName} className="bg-zinc-900/60 border border-zinc-800/80 rounded-lg p-3 space-y-2">
                <div className="text-zinc-100 text-xs font-bold border-b border-zinc-800 pb-1.5 flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 rounded-full bg-amber-400 animate-pulse" />
                  {tbl.tableName}
                </div>
                
                {tbl.addedColumns.length > 0 && (
                  <div className="text-[11px] text-zinc-400">
                    <span className="text-emerald-400 font-bold mr-1">Eklendi:</span>
                    {tbl.addedColumns.join(', ')}
                  </div>
                )}
                
                {tbl.removedColumns.length > 0 && (
                  <div className="text-[11px] text-zinc-400">
                    <span className="text-rose-400 font-bold mr-1 line-through">Silindi:</span>
                    <span className="line-through decoration-rose-500/30">{tbl.removedColumns.join(', ')}</span>
                  </div>
                )}

                {tbl.modifiedColumns.length > 0 && (
                  <div className="text-[11px] text-zinc-400">
                    <span className="text-amber-400 font-bold mr-1">Değişti:</span>
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
