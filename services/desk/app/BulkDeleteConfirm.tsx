'use client';

import { useState } from 'react';

/**
 * Namines Desk v2 §E4.1 / 34-SENDEN-BEKLENENLER.md madde 12 — toplu silme
 * geri alınamaz. 10'dan fazla satır seçilince kullanıcı onay kutusuna
 * "SİL" YAZMALI — Namines Bot'un yanlış yazılmış bir komutu ("aprove" →
 * "approve") TAHMİN ETMEME kararıyla aynı ilke: belirsiz bir onayı makine
 * tahmin etmez, kullanıcı AÇIKÇA yazar.
 *
 * 10'dan AZ seçimde bu modal hiç açılmaz — Desk.tsx tarayıcının kendi
 * `confirm()`'ünü kullanmaya devam eder (tekil silmeyle aynı, tutarlı UX).
 */
const CONFIRM_WORD = 'SİL';
const BULK_DELETE_THRESHOLD = 10;

export { BULK_DELETE_THRESHOLD };

export default function BulkDeleteConfirm({ count, onCancel, onConfirm }: {
  count: number;
  onCancel: () => void;
  onConfirm: () => Promise<void>;
}) {
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canConfirm = typed.trim() === CONFIRM_WORD;

  async function handleConfirm() {
    if (!canConfirm) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Silinemedi.');
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onCancel(); }}>
      <div className="dialog">
        <h2>{count} satır kalıcı olarak silinecek</h2>
        <p className="hint">
          Bu işlem geri alınamaz. Devam etmek için aşağıya <b>{CONFIRM_WORD}</b> yazın.
        </p>

        {error && <div className="notice notice-error">{error}</div>}

        <div className="field">
          <label htmlFor="confirm-word">Onay</label>
          <input
            id="confirm-word" type="text" value={typed} autoFocus
            placeholder={CONFIRM_WORD}
            onChange={e => setTyped(e.target.value)}
          />
        </div>

        <div className="dialog-actions">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>Vazgeç</button>
          <button type="button" className="btn btn-danger" disabled={!canConfirm || busy} onClick={handleConfirm}>
            {busy ? 'Siliniyor…' : `${count} satırı sil`}
          </button>
        </div>
      </div>
    </div>
  );
}
