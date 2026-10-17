'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Prism from 'prismjs';
import 'prismjs/themes/prism-tomorrow.css';
import 'prismjs/components/prism-clike';
import 'prismjs/components/prism-javascript';
import 'prismjs/components/prism-typescript';
import 'prismjs/components/prism-csharp';
import 'prismjs/components/prism-python';
import 'prismjs/components/prism-sql';
import 'prismjs/components/prism-json';
import 'prismjs/components/prism-markdown';
import { AlertTriangle, Package, Boxes } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { Panel, PanelBar, ActionButton, PanelEmpty } from './PanelKit';
import Select from '../ui/Select';

function resolvePrismLang(fileName: string | null, selectedTarget: string): string {
  const lower = (fileName || '').toLowerCase();
  if (lower.endsWith('.ts') || lower.endsWith('.tsx')) return 'typescript';
  if (lower.endsWith('.js') || lower.endsWith('.jsx') || lower.endsWith('.mjs')) return 'javascript';
  if (lower.endsWith('.cs')) return 'csharp';
  if (lower.endsWith('.py')) return 'python';
  if (lower.endsWith('.sql')) return 'sql';
  if (lower.endsWith('.json')) return 'json';
  if (lower.endsWith('.md')) return 'markdown';
  if (selectedTarget.includes('typescript') || selectedTarget.includes('drizzle') || selectedTarget.includes('zod') || selectedTarget.includes('kysely') || selectedTarget.includes('typeorm')) return 'typescript';
  if (selectedTarget.includes('python') || selectedTarget.includes('sqlalchemy') || selectedTarget.includes('django') || selectedTarget.includes('pydantic')) return 'python';
  if (selectedTarget.includes('csharp') || selectedTarget.includes('efcore') || selectedTarget.includes('dapper')) return 'csharp';
  if (selectedTarget.includes('sql')) return 'sql';
  return 'clike';
}

interface Props {
  schema: DatabaseSchema;
  dbType: string;
}

/**
 * 12-CODEGEN-EJECT.md — hedef seçimi ve önizleme.
 *
 * Hedef listesi arka uçtan geliyor: istemcide ikinci bir kopya tutmak, yeni bir
 * hedef eklendiğinde UI'ın onu göstermemesi demek olurdu.
 *
 * Uyarılar kod alanının ÜSTÜNDE ve daraltılamaz — hedefin ifade edemediği yapılar
 * (CHECK kısıtları, index'ler) çıktıda yok, ve bunu indirdikten sonra öğrenmek iş
 * işten geçtikten sonra olurdu (PrismaPreview ile aynı karar).
 */
export default function EjectPanel({ schema, dbType }: Props) {
  const showToast = useToastStore(s => s.showToast);
  const codeRef = useRef<HTMLElement>(null);

  const [targets, setTargets] = useState<{ target: string; name: string }[]>([]);
  const [selected, setSelected] = useState<string>('types.typescript');
  const [files, setFiles] = useState<Record<string, string>>({});
  const [activeFile, setActiveFile] = useState<string | null>(null);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isDownloading, setIsDownloading] = useState(false);

  const hasTables = !!schema?.tables?.length;

  useEffect(() => {
    schemaService.ejectTargets().then(setTargets).catch(() => setTargets([]));
  }, []);

  const load = useCallback(async () => {
    if (!hasTables) return;
    setIsLoading(true);
    setError(null);
    try {
      const result = await schemaService.eject(selected, schema, dbType);
      setFiles(result.files);
      setWarnings(result.warnings ?? []);
      setActiveFile(Object.keys(result.files)[0] ?? null);
    } catch (err: unknown) {
      // Hedefin bu motoru desteklememesi (ör. Drizzle + Oracle) buraya düşer;
      // sunucunun mesajı nedeni tam olarak söylüyor, genel bir hata söylemezdi.
      const response = (err as { response?: { data?: { error?: string; message?: string; detail?: string } | string } })?.response;
      let errorMsg = 'This target could not be generated.';
      if (typeof response?.data === 'string') {
        errorMsg = response.data;
      } else if (response?.data?.error) {
        errorMsg = response.data.error;
      } else if (response?.data?.detail) {
        errorMsg = response.data.detail;
      } else if (response?.data?.message) {
        errorMsg = response.data.message;
      } else if ((err as Error)?.message) {
        errorMsg = (err as Error).message;
      }
      setError(errorMsg);
      setFiles({});
      setWarnings([]);
      setActiveFile(null);
    } finally {
      setIsLoading(false);
    }
  }, [selected, schema, dbType, hasTables]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (codeRef.current && activeFile) Prism.highlightElement(codeRef.current);
  }, [activeFile, files]);

  const handleDownload = async () => {
    setIsDownloading(true);
    try {
      const blob = await schemaService.ejectZip(selected, schema, dbType);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${schema.name || 'Schema'}_${selected.replace('.', '-')}.zip`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast('Export downloaded.', 'success');
    } catch {
      showToast('Export could not be downloaded.', 'error');
    } finally {
      setIsDownloading(false);
    }
  };

  if (!hasTables) {
    return (
      <Panel scroll={false}>
        <PanelEmpty
          icon={Boxes}
          title="No tables yet"
          hint="Add at least one table on the canvas to export it to another stack."
        />
      </Panel>
    );
  }

  const fileNames = Object.keys(files);

  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar
          left={
            <Select
              aria-label="Export target"
              value={selected}
              onChange={e => setSelected(e.target.value)}
              className="min-w-[220px]"
            >
              {targets.map(t => (
                <option key={t.target} value={t.target}>{t.name}</option>
              ))}
            </Select>
          }
        >
          <ActionButton icon={Package} onClick={handleDownload} busy={isDownloading} tone="primary">
            Download .zip
          </ActionButton>
        </PanelBar>

        {warnings.length > 0 && (
          <details className="shrink-0 border-b border-line bg-surface-800/90 px-3.5 py-2.5 group">
            <summary className="flex items-center justify-between gap-2 cursor-pointer list-none select-none">
              <div className="flex items-center gap-2 min-w-0">
                <AlertTriangle className="w-3.5 h-3.5 shrink-0 text-warning-text" />
                <p className="text-[11px] font-semibold text-content-primary truncate">
                  Not everything could be expressed in this target ({warnings.length} DB-level constraint{warnings.length === 1 ? '' : 's'} omitted)
                </p>
              </div>
              <span className="text-[10px] font-mono text-content-muted shrink-0 group-open:hidden">Show details</span>
              <span className="text-[10px] font-mono text-content-muted shrink-0 hidden group-open:inline">Hide details</span>
            </summary>
            <ul className="mt-2 max-h-28 overflow-y-auto space-y-1 pl-5">
              {warnings.map((warning, i) => (
                <li key={i} className="text-[10px] font-mono text-content-secondary leading-relaxed">
                  • {warning}
                </li>
              ))}
            </ul>
          </details>
        )}

        {fileNames.length > 1 && (
          <div className="shrink-0 flex items-center gap-1.5 px-3 py-2 border-b border-line bg-surface-800/60 backdrop-blur-sm overflow-x-auto">
            {fileNames.map(name => (
              <button
                key={name}
                onClick={() => setActiveFile(name)}
                className={`shrink-0 px-3 py-1 rounded-[var(--radius-control)] text-xs font-mono transition-all cursor-pointer whitespace-nowrap border ${
                  activeFile === name
                    ? 'bg-surface-700 text-content-primary font-semibold border-line-strong shadow-xs'
                    : 'text-content-muted hover:text-content-primary hover:bg-surface-700/50 border-transparent'
                }`}
              >
                {name}
              </button>
            ))}
          </div>
        )}

        <div className="flex-1 min-h-0 overflow-auto bg-surface-900">
          {error ? (
            <div className="h-full flex flex-col items-center justify-center p-6 text-center animate-in fade-in duration-150">
              <div className="w-12 h-12 rounded-full bg-surface-800 border border-line-strong flex items-center justify-center mb-3 shadow-md">
                <AlertTriangle className="w-5 h-5 text-warning-text" />
              </div>
              <h3 className="text-sm font-bold text-content-primary mb-1">Target Incompatible</h3>
              <p className="text-xs text-content-secondary max-w-md leading-relaxed mb-4">{error}</p>
              <div className="flex flex-wrap items-center justify-center gap-2">
                <button
                  type="button"
                  onClick={() => setSelected('types.typescript')}
                  className="px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line-strong text-xs font-medium text-content-primary transition-colors cursor-pointer shadow-xs"
                >
                  Switch to TypeScript Types
                </button>
                <button
                  type="button"
                  onClick={() => setSelected('contract.prisma')}
                  className="px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line-strong text-xs font-medium text-content-primary transition-colors cursor-pointer shadow-xs"
                >
                  Switch to Prisma
                </button>
              </div>
            </div>
          ) : (
            <pre className="!bg-transparent !m-0 !p-4 !text-xs !leading-relaxed text-content-primary font-mono">
              <code ref={codeRef} className={`language-${resolvePrismLang(activeFile, selected)} text-content-primary`}>
                {isLoading && !activeFile ? '// generating…' : (activeFile ? files[activeFile] : '')}
              </code>
            </pre>
          )}
        </div>
      </div>
    </Panel>
  );
}
