'use client';

import { useRef, useState } from 'react';
import { FileCode2, X, Loader2, AlertTriangle, CheckCircle2, Upload, GitCompare } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { schemaService } from '../../../services/api';
import { CodeExtractionResponse } from '../../../types/codeSchema';
import { errorMessage } from '../../../lib/errors';
import { useEscapeKey } from '../../../hooks/useEscapeKey';

interface Props {
  isOpen: boolean;
  onClose: () => void;
  /**
   * Sema GERCEKTEN uygulandiginda cagrilir (iptal edildiginde DEGIL).
   *
   * Neden `onClose`'tan ayri: `/new` ekraninda bu panel "prompt yazmadan
   * basla" yolunun bir adimi ve sema geldiginde canvas'a gecilmesi gerekiyor.
   * `onClose` iptal ederken de calisiyor; ona baglanmak, kullanici vazgectiginde
   * de canvas'a atmak demekti.
   */
  onApplied?: () => void;
}

/**
 * second-phase/11-KODDAN-SEMA.md — bir depodaki Prisma/EF Core dosyalarından
 * şemayı çıkarır ve canvas'takiyle farkını gösterir.
 *
 * <b>Kod OKUNUR, değiştirilmez</b> ve hiçbir migration ÇALIŞTIRILMAZ —
 * doc'un iki açık yasağı. Bu panel yalnızca dosya içeriğini metin olarak
 * sunucuya gönderir.
 *
 * <b>Kısmi sonuç gizlenmiyor:</b> kaç model okundu ve kaç tanesi neden
 * okunamadı, ikisi de aynı ekranda. Yalnızca başarıyı göstermek, kullanıcıya
 * olmayan bir tam resim sunar ve o resme dayanıp "kodum veritabanımla uyumlu"
 * sonucuna varır.
 */
export default function CodeImportPanel({ isOpen, onClose, onApplied }: Props) {
  const schema = useSchemaStore(s => s.schema);
  const dbType = useSchemaStore(s => s.dbType);
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const showToast = useToastStore(s => s.showToast);

  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [result, setResult] = useState<CodeExtractionResponse | null>(null);

  const handleFiles = async (fileList: FileList | null) => {
    if (!fileList || fileList.length === 0) return;

    setIsLoading(true);
    setResult(null);
    try {
      const files: Record<string, string> = {};
      await Promise.all(
        Array.from(fileList).map(async f => { files[f.name] = await f.text(); })
      );

      // Canvas'ta şema varsa karşılaştırma da istenir — asıl değer burada:
      // "kodun şunu diyor, veritabanında şu var".
      const response = await schemaService.extractFromCode(
        files,
        schema && schema.tables.length > 0 ? schema : undefined,
        dbType,
      );
      setResult(response);
    } catch (err) {
      const message = errorMessage(err, 'Could not read a schema from those files.');
      showToast(message, 'error');
    } finally {
      setIsLoading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const applyToCanvas = () => {
    if (!result) return;
    loadFromSchema(result.schema, undefined, true);
    showToast(`Loaded ${result.parsedCount} model(s) from ${result.format === 'prisma' ? 'Prisma' : 'EF Core'}.`, 'success');
    onClose();
    onApplied?.();
  };

  useEscapeKey(isOpen, onClose);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200 select-none">
      <div className="bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] w-[90vw] max-w-xl max-h-[85vh] flex flex-col shadow-2xl overflow-hidden">
        <div className="border-b border-line bg-surface-800/40 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-7 w-7 bg-surface-700 border border-line-strong rounded-[var(--radius-control)] flex items-center justify-center text-content-primary">
              <FileCode2 className="w-3.5 h-3.5" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">Schema from Code</h2>
              <p className="text-micro text-content-muted">Extract tables from Prisma, EF Core or SQL migrations (read-only)</p>
            </div>
          </div>
          <button onClick={onClose} className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept=".prisma,.cs,.sql"
            className="hidden"
            onChange={e => handleFiles(e.target.files)}
          />

          {!result && (
            <button
              onClick={() => fileInputRef.current?.click()}
              disabled={isLoading}
              className="flex flex-col items-center justify-center gap-3 w-full py-12 px-6 rounded-[var(--radius-card)] border-2 border-dashed border-line-strong hover:border-content-primary bg-surface-800/40 hover:bg-surface-800/70 transition-all cursor-pointer disabled:opacity-50 group"
            >
              <div className="w-10 h-10 rounded-full bg-surface-700 border border-line flex items-center justify-center text-content-muted group-hover:text-content-primary group-hover:scale-105 transition-all">
                {isLoading ? <Loader2 className="w-5 h-5 animate-spin text-content-primary" /> : <Upload className="w-5 h-5" />}
              </div>
              <div className="text-center">
                <span className="text-xs font-bold text-content-primary block">
                  {isLoading ? 'Extracting schema models…' : 'Select schema.prisma, C# .cs entity files, or .sql scripts'}
                </span>
                <span className="text-micro text-content-muted mt-1 block">
                  Files are analyzed purely in-memory — code is never compiled or executed.
                </span>
              </div>
            </button>
          )}

          {result && (
            <div className="flex flex-col gap-4">
              <div className="flex items-center gap-3 text-xs">
                <span className="flex items-center gap-1.5 text-success-text font-bold">
                  <CheckCircle2 className="w-3.5 h-3.5" />
                  {result.parsedCount} model(s) extracted
                </span>
                {result.skippedCount > 0 && (
                  <span className="flex items-center gap-1.5 text-warning font-bold">
                    <AlertTriangle className="w-3.5 h-3.5" />
                    {result.skippedCount} skipped
                  </span>
                )}
                <span className="text-micro text-content-muted ml-auto font-mono uppercase bg-surface-700 border border-line px-2 py-0.5 rounded-full font-bold">
                  {result.format}
                </span>
              </div>

              <div className="flex flex-wrap gap-1.5">
                {result.parsedModels.map(m => (
                  <span key={m} className="font-mono text-micro text-content-primary bg-surface-800 border border-line px-2.5 py-1 rounded-full font-bold">{m}</span>
                ))}
              </div>

              {result.skipped.length > 0 && (
                <div className="border border-line bg-surface-800/60 rounded-[var(--radius-card)] p-3.5">
                  <p className="text-micro uppercase tracking-wider text-warning font-bold mb-1.5">Could not be read</p>
                  <ul className="flex flex-col gap-1">
                    {result.skipped.map((s, i) => (
                      <li key={i} className="text-micro text-content-muted">
                        <span className="font-mono text-content-primary font-bold">{s.name}</span> — {s.reason}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {result.drift && (
                <div className="border border-line bg-surface-800/60 rounded-[var(--radius-card)] p-3.5">
                  <p className="flex items-center gap-1.5 text-micro uppercase tracking-wider text-content-muted font-bold mb-2">
                    <GitCompare className="w-3.5 h-3.5" />
                    Code vs. Current Canvas
                  </p>
                  {!result.drift.hasDrift ? (
                    <p className="text-xs text-success-text font-medium">No drift — the code models match the current canvas schema perfectly.</p>
                  ) : (
                    <div className="flex flex-col gap-1.5">
                      {result.drift.affectedTables.map((t, i) => (
                        <div key={i} className="text-micro text-content-muted">
                          <span className="font-mono text-content-primary font-bold">{t.tableName}</span>
                          <span className="text-content-muted"> — {t.kind.toLowerCase()}</span>
                          {t.changedColumns.length > 0 && (
                            <span className="text-content-subtle"> ({t.changedColumns.join(', ')})</span>
                          )}
                        </div>
                      ))}
                      {result.drift.breakingChanges.map((b, i) => (
                        <div key={`b${i}`} className="text-micro text-danger-text font-mono font-semibold">{b.description}</div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              <div className="flex items-center justify-between pt-2 border-t border-line">
                <button
                  onClick={() => { setResult(null); fileInputRef.current?.click(); }}
                  className="px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line text-xs font-semibold text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
                >
                  Choose Different Files
                </button>
                <button
                  onClick={applyToCanvas}
                  className="bg-content-primary hover:bg-content-secondary text-surface-900 px-4 py-1.5 rounded-[var(--radius-control)] text-xs font-bold transition-all shadow-sm cursor-pointer"
                >
                  Load onto Canvas
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
