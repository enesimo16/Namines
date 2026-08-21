'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Prism from 'prismjs';
import 'prismjs/themes/prism-tomorrow.css';
import { AlertTriangle, Package, Boxes } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { Panel, PanelBar, ActionButton, PanelEmpty } from './PanelKit';

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
      const response = (err as { response?: { data?: { error?: string } } }).response;
      setError(response?.data?.error ?? 'This target could not be generated.');
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
            <select
              value={selected}
              onChange={e => setSelected(e.target.value)}
              className="bg-surface-800 border border-content-primary/15 rounded-lg px-2 py-1 text-[11px] text-content-primary outline-none focus:border-accent/50"
            >
              {targets.map(t => (
                <option key={t.target} value={t.target}>{t.name}</option>
              ))}
            </select>
          }
        >
          <ActionButton icon={Package} onClick={handleDownload} busy={isDownloading} tone="primary">
            Download .zip
          </ActionButton>
        </PanelBar>

        {warnings.length > 0 && (
          <div className="shrink-0 border-b border-content-primary/10 bg-surface-800 px-3 py-2">
            <div className="flex items-start gap-2">
              <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0 text-content-primary" />
              <div className="min-w-0">
                <p className="text-[11px] font-semibold text-content-primary">
                  Not everything could be expressed in this target
                </p>
                <ul className="mt-1 space-y-0.5">
                  {warnings.map((warning, i) => (
                    <li key={i} className="text-[10px] font-mono text-content-secondary leading-relaxed">
                      • {warning}
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          </div>
        )}

        {fileNames.length > 1 && (
          <div className="shrink-0 flex items-center gap-1 px-3 py-1.5 border-b border-content-primary/10 overflow-x-auto">
            {fileNames.map(name => (
              <button
                key={name}
                onClick={() => setActiveFile(name)}
                className={`shrink-0 px-2 py-1 rounded-md text-[10px] font-mono transition-colors ${
                  activeFile === name
                    ? 'bg-surface-600 text-content-primary'
                    : 'text-content-muted hover:text-content-secondary'
                }`}
              >
                {name}
              </button>
            ))}
          </div>
        )}

        <div className="flex-1 min-h-0 overflow-auto bg-surface-900">
          {error ? (
            <div className="h-full flex items-center justify-center px-6">
              <p className="text-[11px] text-content-muted text-center max-w-md leading-relaxed">{error}</p>
            </div>
          ) : (
            <pre className="!bg-transparent !m-0 !p-3 !text-[11px] !leading-relaxed">
              <code ref={codeRef} className="language-none">
                {isLoading && !activeFile ? '// generating…' : (activeFile ? files[activeFile] : '')}
              </code>
            </pre>
          )}
        </div>
      </div>
    </Panel>
  );
}
