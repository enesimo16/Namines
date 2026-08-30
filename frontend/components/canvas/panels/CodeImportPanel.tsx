'use client';

import { useRef, useState } from 'react';
import { FileCode2, X, Loader2, AlertTriangle, CheckCircle2, Upload, GitCompare } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { schemaService } from '../../../services/api';
import { CodeExtractionResponse } from '../../../types/codeSchema';

interface Props {
  isOpen: boolean;
  onClose: () => void;
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
export default function CodeImportPanel({ isOpen, onClose }: Props) {
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
    } catch (err: any) {
      const message = err?.response?.data?.message || 'Could not read a schema from those files.';
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
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200">
      <div className="bg-surface-800 border border-content-primary/12 rounded-2xl w-[90vw] max-w-xl max-h-[85vh] flex flex-col shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] overflow-hidden">
        <div className="border-b border-content-primary/10 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 bg-surface-600 border border-content-primary/10 rounded-lg flex items-center justify-center">
              <FileCode2 className="w-4 h-4 text-content-primary" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">Schema from Code</h2>
              <p className="text-[11px] text-content-muted">Prisma, EF Core or SQL migrations — read only, never executed.</p>
            </div>
          </div>
          <button onClick={onClose} className="p-1.5 hover:bg-white/[0.06] rounded-lg text-content-subtle hover:text-content-primary transition-colors" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          <input
            ref={fileInputRef}
            type="file"
            multiple
            // .sql da kabul ediliyor: ham CREATE TABLE ayrıştırıcısı var
            // (second-phase/11) ve Supabase migration akışı (12) buna dayanıyor.
            // Filtre eksik kaldığında ayrıştırıcı çalışıyordu ama kullanıcı
            // dosyayı seçicide GÖREMİYORDU — yani o yol arayüzden erişilemezdi.
            accept=".prisma,.cs,.sql"
            className="hidden"
            onChange={e => handleFiles(e.target.files)}
          />

          {!result && (
            <button
              onClick={() => fileInputRef.current?.click()}
              disabled={isLoading}
              className="flex flex-col items-center justify-center gap-2 w-full py-10 rounded-xl border border-dashed border-content-primary/20 text-content-muted hover:text-content-primary hover:border-content-primary/40 transition-colors disabled:opacity-50"
            >
              {isLoading ? <Loader2 className="w-5 h-5 animate-spin" /> : <Upload className="w-5 h-5" />}
              <span className="text-xs font-semibold">
                {isLoading ? 'Reading…' : 'Select schema.prisma, .cs entity files, or .sql migrations'}
              </span>
              <span className="text-[10px]">Nothing is executed — the files are only read as text.</span>
            </button>
          )}

          {result && (
            <div className="flex flex-col gap-4">
              {/* Dürüst kısmi rapor — okunan VE okunamayan birlikte. */}
              <div className="flex items-center gap-3 text-xs">
                <span className="flex items-center gap-1.5 text-success-text">
                  <CheckCircle2 className="w-3.5 h-3.5" />
                  {result.parsedCount} model read
                </span>
                {result.skippedCount > 0 && (
                  <span className="flex items-center gap-1.5 text-warning">
                    <AlertTriangle className="w-3.5 h-3.5" />
                    {result.skippedCount} not understood
                  </span>
                )}
                <span className="text-content-subtle ml-auto font-mono text-[10px] uppercase">{result.format}</span>
              </div>

              <div className="flex flex-wrap gap-1.5">
                {result.parsedModels.map(m => (
                  <span key={m} className="font-mono text-[11px] text-content-primary bg-surface-700 border border-content-primary/10 px-2 py-1 rounded-md">{m}</span>
                ))}
              </div>

              {result.skipped.length > 0 && (
                <div className="border border-warning/25 bg-warning/[0.06] rounded-xl p-3">
                  <p className="text-[10px] uppercase tracking-wider text-warning font-bold mb-1.5">Could not be read</p>
                  <ul className="flex flex-col gap-1">
                    {result.skipped.map((s, i) => (
                      <li key={i} className="text-[11px] text-content-muted">
                        <span className="font-mono text-content-secondary">{s.name}</span> — {s.reason}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {result.drift && (
                <div className="border border-content-primary/12 rounded-xl p-3">
                  <p className="flex items-center gap-1.5 text-[10px] uppercase tracking-wider text-content-subtle font-bold mb-2">
                    <GitCompare className="w-3 h-3" />
                    Code vs. this canvas
                  </p>
                  {!result.drift.hasDrift ? (
                    <p className="text-xs text-success-text">No drift — the code matches what is on the canvas.</p>
                  ) : (
                    <div className="flex flex-col gap-1.5">
                      {result.drift.affectedTables.map((t, i) => (
                        <div key={i} className="text-[11px] text-content-muted">
                          <span className="font-mono text-content-primary">{t.tableName}</span>
                          <span className="text-content-subtle"> — {t.kind.toLowerCase()}</span>
                          {t.changedColumns.length > 0 && (
                            <span className="text-content-subtle"> ({t.changedColumns.join(', ')})</span>
                          )}
                        </div>
                      ))}
                      {result.drift.breakingChanges.map((b, i) => (
                        <div key={`b${i}`} className="text-[11px] text-danger-text">{b.description}</div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              <div className="flex items-center justify-between pt-1">
                <button
                  onClick={() => { setResult(null); fileInputRef.current?.click(); }}
                  className="text-xs text-content-muted hover:text-content-primary transition-colors"
                >
                  Choose different files
                </button>
                <button
                  onClick={applyToCanvas}
                  className="bg-content-primary hover:bg-content-secondary text-surface-900 px-4 py-2 rounded-lg text-xs font-semibold transition-all"
                >
                  Load onto canvas
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
