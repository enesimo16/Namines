'use client';

import React, { useRef, useState } from 'react';
// lucide marka ikonlarını kaldırdı (`Github` artık yok); depo anlamını
// taşıyan genel ikon kullanılıyor.
import { X, FolderGit2, Loader2, AlertTriangle } from 'lucide-react';
import { useFocusTrap } from '../../hooks/useFocusTrap';
import { errorMessage } from '../../lib/errors';
import type { RepositoryScanResult } from '../../types/source';

interface GithubScanModalProps {
  onScan: (repoUrl: string, branch?: string) => Promise<RepositoryScanResult>;
  onImport: (result: RepositoryScanResult) => void;
  onClose: () => void;
}

/**
 * github/01-DEPO-TARAMA.md — depo linkinden şema.
 *
 * <b>Taramanın kendisi burada DEĞİL:</b> bileşen yalnızca `onScan`'i çağırıyor.
 * Böylece ekranın davranışı (boş adres, hata mesajı, atlananların gösterimi)
 * ağ olmadan test edilebiliyor — ve testler bunları kilitliyor.
 */
export function GithubScanModal({ onScan, onImport, onClose }: GithubScanModalProps) {
  const [repoUrl, setRepoUrl] = useState('');
  const [branch, setBranch] = useState('');
  const [scanning, setScanning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<RepositoryScanResult | null>(null);

  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(true, modalRef);

  const scan = async () => {
    if (!repoUrl.trim()) return;

    setScanning(true);
    setError(null);
    setResult(null);

    try {
      setResult(await onScan(repoUrl.trim(), branch.trim() || undefined));
    } catch (e) {
      // Sunucunun mesajı OLDUĞU GİBİ gösteriliyor: "private mi, yok mu",
      // "kota doldu" ve "formatı tanıyamadım" birbirinden farklı şeyler ve
      // kullanıcının ne yapacağını yalnızca o metin söylüyor.
      //
      // `e.message` YETMİYOR: axios hatasının mesajı "Request failed with
      // status code 400" oluyor ve sunucunun metni `response.data.message`'ta
      // kalıyor. Canlı doğrulamada kullanıcı tam olarak o işe yaramaz cümleyi
      // gördü; birim test düz bir Error taklit ettiği için görmemişti.
      setError(errorMessage(e, 'The scan failed.'));
    } finally {
      setScanning(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/80 backdrop-blur-sm">
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-label="Import a schema from a GitHub repository"
        className="w-full max-w-lg max-h-[85vh] overflow-y-auto glass-panel rounded-[var(--radius-modal)] p-5 flex flex-col gap-4"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2">
            <FolderGit2 className="w-4 h-4 text-content-secondary" />
            <h2 className="text-body-lg text-content-primary">GitHub repository</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="p-1 text-content-muted hover:text-content-primary transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <p className="text-micro text-content-muted">
          The schema is read from the code in a public repository — Prisma, EF Core or SQL
          migrations. Nothing is cloned, written or executed.
        </p>

        <div className="flex flex-col gap-2">
          <input
            aria-label="Repository URL"
            type="text"
            value={repoUrl}
            onChange={(e) => setRepoUrl(e.target.value)}
            placeholder="github.com/owner/name"
            disabled={scanning}
            className="glass-input rounded-[var(--radius-control)] px-3 py-2 text-sm text-content-primary placeholder:text-content-muted focus:outline-none"
          />
          <input
            aria-label="Branch (optional)"
            type="text"
            value={branch}
            onChange={(e) => setBranch(e.target.value)}
            placeholder="Branch — leave empty for the default"
            disabled={scanning}
            className="glass-input rounded-[var(--radius-control)] px-3 py-2 text-sm text-content-primary placeholder:text-content-muted focus:outline-none"
          />
        </div>

        <button
          type="button"
          onClick={scan}
          disabled={scanning}
          className="self-start flex items-center gap-2 px-3 py-2 rounded-[var(--radius-control)] glass-button text-sm text-content-primary disabled:opacity-50"
        >
          {scanning && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
          Scan
        </button>

        {error && (
          <p className="text-sm text-danger-text border border-line-strong rounded-[var(--radius-control)] p-3">
            {error}
          </p>
        )}

        {result && (
          <div className="flex flex-col gap-3">
            <p className="text-micro text-content-muted">
              Read {result.format} from <span className="text-content-secondary">{result.branch}</span>.
            </p>

            {result.treeTruncated && (
              <p className="flex items-start gap-2 text-micro text-content-secondary">
                <AlertTriangle className="w-3.5 h-3.5 shrink-0 mt-px" />
                This repository was too large to list in full — some files were never seen.
              </p>
            )}

            <div className="flex flex-col gap-1">
              <span className="text-micro uppercase tracking-wide text-content-muted">
                Read ({result.parsedFiles.length})
              </span>
              {result.parsedFiles.map((file) => (
                <span key={file} className="text-sm text-content-primary">{file}</span>
              ))}
            </div>

            {/* Atlananlar KAPATILAMAZ ve gerekçesiyle durur: kullanıcının her
                şeyi gördüğünü sanması, eksik şemayla ilerlemesinin tek
                sebebidir (01-DEPO-TARAMA.md). */}
            {result.skipped.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <span className="text-micro uppercase tracking-wide text-content-muted">
                  Skipped ({result.skipped.length})
                </span>
                {result.skipped.map((skip) => (
                  <div key={skip.name} className="flex flex-col">
                    <span className="text-sm text-content-secondary">{skip.name}</span>
                    <span className="text-micro text-content-muted">{skip.reason}</span>
                  </div>
                ))}
              </div>
            )}

            <button
              type="button"
              onClick={() => onImport(result)}
              className="self-start px-3 py-2 rounded-[var(--radius-control)] glass-button text-sm text-accent-text border-accent"
            >
              Import into canvas
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
