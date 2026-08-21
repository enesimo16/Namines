import React, { useCallback, useEffect, useRef, useState } from 'react';
import Prism from 'prismjs';
import 'prismjs/themes/prism-tomorrow.css';
import { AlertTriangle, Package, RefreshCw } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { Panel, PanelBar, ActionButton, PanelEmpty } from './PanelKit';

interface PrismaPreviewProps {
  schema: DatabaseSchema;
  dbType: string;
}

/**
 * Prisma şeması önizlemesi.
 *
 * Şema İSTEMCİDE ÜRETİLMEZ, arka uçtan çekilir. EfCorePreview bunun tersini
 * yapıyor (kendi C# kodunu üretiyor) ve bedeli görünür: yalnızca ilk tabloyu
 * gösteriyor, yani önizleme ile indirilen ZIP aynı şey değil. Prisma'da bu
 * ayrışmaya izin verilmedi — üretici 21 testle (4'ü gerçek `prisma validate`)
 * doğrulanmış tek kopya, ve `warnings` yalnızca oradan gelebilir.
 */
export default function PrismaPreview({ schema, dbType }: PrismaPreviewProps) {
  const codeRef = useRef<HTMLElement>(null);
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
      // Oracle 400 döner (Prisma'nın Oracle provider'ı yok). Sunucunun mesajı
      // olduğu gibi gösterilir — genel bir "hata oluştu" kullanıcıya nedeni
      // söylemez ve burada neden tam olarak eyleme geçirilebilir bir bilgidir.
      const response = (err as { response?: { data?: { error?: string } } }).response;
      setError(response?.data?.error ?? 'Prisma schema could not be generated.');
      setContent('');
      setWarnings([]);
    } finally {
      setIsLoading(false);
    }
  }, [schema, dbType, hasTables]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (codeRef.current && content) Prism.highlightElement(codeRef.current);
  }, [content]);

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
      <Panel scroll={false}>
        <PanelEmpty
          icon={Package}
          title="No tables yet"
          hint="Add at least one table on the canvas to generate a Prisma schema."
        />
      </Panel>
    );
  }

  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar
          left={
            <>
              <span className="text-[11px] font-mono text-content-secondary truncate">prisma/schema.prisma</span>
              <span className="text-[10px] font-mono text-content-muted shrink-0">{dbType}</span>
            </>
          }
        >
          <ActionButton icon={RefreshCw} onClick={load} busy={isLoading}>
            Refresh
          </ActionButton>
          <ActionButton icon={Package} onClick={handleDownload} busy={isDownloading} tone="primary">
            Download .zip
          </ActionButton>
        </PanelBar>

        {/*
          Uyarılar kod alanının ÜSTÜNDE ve daraltılamaz. Prisma'nın ifade edemediği
          her yapı (CHECK kısıtı, kısmi index) çıktıda YOKTUR; bu dosyadan
          `prisma db push` çalıştıran biri onları veritabanından düşürür. Uyarıyı
          kaydırma alanının içine koymak, görülmeden kaybolmasına izin vermek olurdu.
        */}
        {warnings.length > 0 && (
          <div className="shrink-0 border-b border-surface-500 bg-surface-800 px-3 py-2">
            <div className="flex items-start gap-2">
              <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0 text-content-primary" />
              <div className="min-w-0">
                <p className="text-[11px] font-semibold text-content-primary">
                  Not everything could be expressed in Prisma
                </p>
                <p className="text-[10px] text-content-muted leading-relaxed mb-1">
                  These are absent from the schema below. Running <code className="font-mono">prisma db push</code>{' '}
                  from this file would drop them from the database.
                </p>
                <ul className="space-y-0.5">
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

        <div className="flex-1 min-h-0 overflow-auto bg-surface-900">
          {error ? (
            <div className="h-full flex items-center justify-center px-6">
              <div className="text-center space-y-1.5 max-w-md">
                <p className="text-[12px] font-semibold text-danger-text">Prisma cannot target this engine</p>
                <p className="text-[11px] text-content-muted leading-relaxed">{error}</p>
              </div>
            </div>
          ) : (
            <pre className="!bg-transparent !m-0 !p-3 !text-[11px] !leading-relaxed">
              <code ref={codeRef} className="language-none">
                {isLoading && !content ? '// generating…' : content}
              </code>
            </pre>
          )}
        </div>
      </div>
    </Panel>
  );
}
