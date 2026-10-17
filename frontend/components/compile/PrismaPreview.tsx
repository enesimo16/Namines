'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, Package, RefreshCw } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { PanelEmpty } from './PanelKit';
import CodeStudioViewer from './CodeStudioViewer';

interface PrismaPreviewProps {
  schema: DatabaseSchema;
  dbType: string;
}

export default function PrismaPreview({ schema, dbType }: PrismaPreviewProps) {
  const showToast = useToastStore(state => state.showToast);

  const [content, setContent] = useState<string>('');
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isDownloading, setIsDownloading] = useState(false);

  const hasTables = !!schema?.tables?.length;

  const load = useCallback(async () => {
    if (!hasTables) return;
    setIsLoading(true);
    setError(null);
    try {
      const result = await schemaService.compilePrisma(schema, dbType);
      setContent(result.schema);
      setWarnings(result.warnings ?? []);
    } catch (err: unknown) {
      const response = (err as { response?: { data?: { error?: string } } }).response;
      setError(response?.data?.error ?? 'Prisma schema could not be generated.');
      setContent('');
      setWarnings([]);
    } finally {
      setIsLoading(false);
    }
  }, [schema, dbType, hasTables]);

  useEffect(() => {
    load();
  }, [load]);

  const handleDownload = async () => {
    setIsDownloading(true);
    try {
      const blob = await schemaService.compilePrismaZip(schema, dbType);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${schema.name || 'Schema'}_Prisma.zip`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast('Prisma schema downloaded.', 'success');
    } catch {
      showToast('Prisma schema could not be downloaded.', 'error');
    } finally {
      setIsDownloading(false);
    }
  };

  if (!hasTables) {
    return (
      <div className="w-full h-full bg-surface-900 border border-line-strong rounded-[var(--radius-card)] flex items-center justify-center">
        <PanelEmpty
          icon={Package}
          title="No tables yet"
          hint="Add at least one table on the canvas to generate a Prisma schema."
        />
      </div>
    );
  }

  if (error) {
    return (
      <div className="w-full h-full bg-surface-900 border border-line-strong rounded-[var(--radius-card)] flex items-center justify-center px-6">
        <div className="text-center space-y-2 max-w-md">
          <div className="w-10 h-10 rounded-full bg-surface-800 border border-line flex items-center justify-center mx-auto text-danger-text">
            <AlertTriangle className="w-5 h-5" />
          </div>
          <p className="text-xs font-semibold text-danger-text">Prisma cannot target this engine</p>
          <p className="text-[11px] text-content-muted leading-relaxed">{error}</p>
          <button
            onClick={load}
            className="tap-44 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 text-content-primary text-xs hover:bg-surface-600 transition-colors inline-flex items-center gap-1.5"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            <span>Try Again</span>
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="w-full h-full flex flex-col overflow-hidden">
      {warnings.length > 0 && (
        <details className="shrink-0 border-b border-line-strong bg-surface-800/90 px-3.5 py-2 group">
          <summary className="flex items-center justify-between gap-2.5 cursor-pointer list-none select-none">
            <div className="flex items-center gap-2 min-w-0">
              <AlertTriangle className="w-4 h-4 shrink-0 text-content-secondary" />
              <p className="text-xs font-semibold text-content-primary truncate">
                Not everything could be expressed in Prisma ({warnings.length} item{warnings.length === 1 ? '' : 's'})
              </p>
            </div>
            <span className="text-[10px] font-mono text-content-muted shrink-0 group-open:hidden">Show details</span>
            <span className="text-[10px] font-mono text-content-muted shrink-0 hidden group-open:inline">Hide details</span>
          </summary>
          <div className="mt-1.5 pl-6">
            <p className="text-[10px] text-content-muted leading-relaxed mb-1">
              These are absent from the schema below. Running <code className="font-mono text-content-secondary">prisma db push</code>{' '}
              from this file would drop them from the database.
            </p>
            <ul className="max-h-28 overflow-y-auto space-y-0.5">
              {warnings.map((warning, i) => (
                <li key={i} className="text-[10px] font-mono text-content-secondary leading-relaxed">
                  • {warning}
                </li>
              ))}
            </ul>
          </div>
        </details>
      )}

      <div className="flex-1 min-h-0">
        <CodeStudioViewer
          code={isLoading && !content ? '// Generating Prisma Schema…' : content}
          language="prisma"
          filename="schema.prisma"
          dialectBadge={`Prisma · ${dbType}`}
          headerControls={
            <button
              onClick={load}
              disabled={isLoading}
              className="tap-44 flex items-center gap-1 px-2 py-1 rounded-[var(--radius-control)] bg-surface-700/80 hover:bg-surface-700 border border-line text-[11px] text-content-secondary transition-colors cursor-pointer"
              title="Refresh Prisma schema"
            >
              <RefreshCw className={`w-3 h-3 ${isLoading ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">Refresh</span>
            </button>
          }
          onDownload={handleDownload}
          downloadLabel="Download .zip"
          isDownloading={isDownloading}
          statusText="Prisma ORM 5.x Schema · Ready to Deploy"
        />
      </div>
    </div>
  );
}
