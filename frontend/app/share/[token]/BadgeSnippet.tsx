'use client';

import { useCallback, useState } from 'react';
import { API_BASE_URL } from '../../../lib/apiConfig';

/**
 * README'ye yapıştırılabilir DBA rozeti (new-phase/23-GTM.md §2 Döngü 2).
 *
 * <b>Rozet ucu (`/api/share/badge/{token}`) çoktandır vardı ama hiçbir yerde
 * GÖRÜNMÜYORDU</b> — kimsenin bilmediği bir uç, olmayan bir özelliktir. Döngü 2'nin
 * tamamı rozetin GitHub README'lerinde görünmesine bağlı, o yüzden eksik olan
 * parça kod değil, hazır Markdown'ı kullanıcının eline vermek.
 *
 * Snippet gösteriliyor VE panoya kopyalanıyor: kopyalama izni olmayan ya da
 * reddedilen bir tarayıcıda buton sessizce hiçbir şey yapmasın diye metin
 * her hâlükârda seçilebilir hâlde duruyor.
 */
export default function BadgeSnippet({ token, projectName }: { token: string; projectName: string }) {
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);

  const pageUrl = typeof window === 'undefined' ? '' : window.location.href;
  const badgeUrl = `${API_BASE_URL}/share/badge/${encodeURIComponent(token)}`;
  const markdown = `[![${projectName} — Namines DBA Score](${badgeUrl})](${pageUrl})`;

  const copy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(markdown);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Pano izni reddedildi; metin zaten ekranda ve elle seçilebilir.
      setCopied(false);
    }
  }, [markdown]);

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="px-3 py-1 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-muted hover:text-content-primary text-xs border border-surface-500 transition-colors"
      >
        README badge
      </button>

      {open && (
        <div className="absolute right-0 top-full mt-2 z-10 w-[420px] max-w-[calc(100vw-2rem)] rounded-[var(--radius-card)] border border-surface-500 bg-surface-800 p-3 shadow-[0_12px_40px_color-mix(in srgb, var(--color-scrim) 45%, transparent)]">
          <p className="text-content-muted text-xs mb-2 leading-relaxed">
            Paste this into your README. The badge reflects the schema&apos;s current
            structural score and updates on its own.
          </p>

          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={badgeUrl}
            alt={`${projectName} DBA score`}
            width={160}
            height={20}
            className="mb-2"
          />

          <code className="block rounded-[var(--radius-control)] bg-surface-900 border border-surface-600 p-2 text-[11px] font-mono text-content-muted break-all select-all">
            {markdown}
          </code>

          <button
            type="button"
            onClick={copy}
            className="mt-2 w-full px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-primary text-xs font-medium transition-colors"
          >
            {copied ? 'Copied' : 'Copy Markdown'}
          </button>
        </div>
      )}
    </div>
  );
}
