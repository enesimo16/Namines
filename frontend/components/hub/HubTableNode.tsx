'use client';

import React, { memo } from 'react';
import { Handle, Position, NodeProps } from '@xyflow/react';
import { Key, Link as LinkIcon, Table2 } from 'lucide-react';
import type { SchemaTable } from '../../types/schema';

export interface HubTableNodeData {
  table: SchemaTable;
  isFocused?: boolean;
}

function HubTableNodeComponent({ data }: NodeProps) {
  const tableData = data as unknown as HubTableNodeData;
  const table = tableData.table;
  const isFocused = tableData.isFocused;

  if (!table) return null;

  return (
    <div
      className={`relative w-[280px] rounded-[var(--radius-card)] bg-surface-850 border transition-all duration-150 shadow-md select-none ${
        isFocused
          ? 'border-accent ring-2 ring-accent/30 shadow-lg'
          : 'border-line hover:border-line-strong'
      }`}
    >
      <Handle
        type="target"
        position={Position.Left}
        id="target"
        className="!w-2.5 !h-2.5 !bg-accent-text !border !border-line-strong"
      />
      <Handle
        type="source"
        position={Position.Right}
        id="source"
        className="!w-2.5 !h-2.5 !bg-accent-text !border !border-line-strong"
      />

      {/* Table Header */}
      <div className="flex items-center justify-between gap-2 px-3.5 py-2.5 border-b border-line bg-surface-800 rounded-t-[var(--radius-card)]">
        <div className="flex items-center gap-2 min-w-0">
          <Table2 className="w-3.5 h-3.5 text-accent-text shrink-0" />
          <span className="font-mono text-xs font-bold text-content-primary truncate">
            {table.name}
          </span>
        </div>
        <span className="text-[10px] font-mono text-content-muted shrink-0 bg-surface-750 px-2 py-0.5 rounded-[var(--radius-control)] border border-line">
          {table.columns?.length ?? 0} cols
        </span>
      </div>

      {/* Columns List */}
      <div className="divide-y divide-line/40 max-h-[300px] overflow-y-auto">
        {table.columns?.map((col) => {
          return (
            <div
              key={col.id || col.name}
              className="flex items-center justify-between gap-2 px-3.5 py-1.5 text-xs hover:bg-surface-750/50 transition-colors"
            >
              <div className="flex items-center gap-1.5 min-w-0">
                {col.isPK ? (
                  <Key className="w-3 h-3 text-accent-text shrink-0" aria-label="Primary Key" />
                ) : col.isFK ? (
                  <LinkIcon className="w-3 h-3 text-content-muted shrink-0" aria-label="Foreign Key" />
                ) : (
                  <span className="w-1.5 h-1.5 rounded-full bg-line-strong shrink-0 ml-1" />
                )}
                <span
                  className={`truncate font-mono ${
                    col.isPK
                      ? 'font-bold text-content-primary'
                      : col.isFK
                      ? 'text-content-secondary font-medium'
                      : 'text-content-secondary'
                  }`}
                >
                  {col.name}
                </span>
              </div>
              <div className="flex items-center gap-1 shrink-0 text-[11px] font-mono text-content-muted">
                <span>{col.type || 'text'}</span>
                {col.length ? <span>({col.length})</span> : null}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export const HubTableNode = memo(HubTableNodeComponent);
