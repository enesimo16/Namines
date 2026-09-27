'use client';

import { useState } from 'react';
import { useT } from '../lib/i18n';
import { rich } from '../lib/rich';

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
const CONFIRM_WORD = 'DELETE';
const BULK_DELETE_THRESHOLD = 10;

export { BULK_DELETE_THRESHOLD };

export default function BulkDeleteConfirm({ count, onCancel, onConfirm }: {
  count: number;
  onCancel: () => void;
  onConfirm: () => Promise<void>;
}) {
  const t = useT();
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
      setError(err instanceof Error ? err.message : t('common.deleteFailed'));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onCancel(); }}>
      <div className="dialog">
        <h2>{t(count === 1 ? 'bulkDelete.titleOne' : 'bulkDelete.titleOther', { count })}</h2>
        <p className="hint">{rich(t('bulkDelete.hint', { word: CONFIRM_WORD }))}</p>

        {error && <div className="notice notice-error">{error}</div>}

        <div className="field">
          <label htmlFor="confirm-word">{t('bulkDelete.confirm')}</label>
          <input
            id="confirm-word" type="text" value={typed} autoFocus
            placeholder={CONFIRM_WORD}
            onChange={e => setTyped(e.target.value)}
          />
        </div>

        <div className="dialog-actions">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="btn btn-danger" disabled={!canConfirm || busy} onClick={handleConfirm}>
            {busy ? t('bulkDelete.deleting') : t(count === 1 ? 'bulkDelete.buttonOne' : 'bulkDelete.buttonOther', { count })}
          </button>
        </div>
      </div>
    </div>
  );
}
