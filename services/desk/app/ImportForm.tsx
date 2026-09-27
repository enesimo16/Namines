'use client';

import { useState } from 'react';
import { DESK_DB_ENGINES } from '../lib/auth';
import { useT } from '../lib/i18n';

/**
 * "Import" akışı — bir Namines projesine canlı veritabanı bağlantısı bağlamak
 * (namines_desk/02-PROJECTS.md §3).
 *
 * Adım 4 atlanamaz: sunucu KAYDETMEDEN ÖNCE gerçekten bağlanıp şemayı okumayı
 * dener (`GatewayKeyController.SetProjectConnection`). Burada yalnızca
 * sunucunun HAM hata mesajı gösteriliyor — "bir hata oluştu" demek, kullanıcıyı
 * yanlış kimlik bilgisiyle erişilemeyen sunucu arasında kör bırakırdı.
 */
export default function ImportForm({
  projectName, onCancel, onSubmit, onUseGround,
}: {
  projectName: string;
  onCancel: () => void;
  onSubmit: (connectionString: string, dbType: string) => Promise<void>;
  /**
   * "Namines Ground ile oluştur" seçilirse çağrılır.
   *
   * <b>Neden burada:</b> bu pencere, kullanıcıya "bu projenin veritabanı
   * nerede" diye sorulan TEK yer. Ground'un işi ise projeye ilk veritabanını
   * vermek — ama Desk bağlantısı olmayan bir projeyi açtırmıyor, dolayısıyla
   * Ground tam da ihtiyaç duyan projeler için erişilemez kalırdı.
   */
  onUseGround: () => void;
}) {
  const t = useT();
  const [connectionString, setConnectionString] = useState('');
  const [dbType, setDbType] = useState<string>(DESK_DB_ENGINES[0]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await onSubmit(connectionString.trim(), dbType);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('importForm.saveFailed'));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onCancel(); }}>
      <form className="dialog" onSubmit={submit}>
        <h2>{t('importForm.title')}</h2>
        <p className="hint">{t('importForm.hint', { name: projectName })}</p>

        {error && <div className="notice notice-error">{error}</div>}

        <div className="field">
          <label htmlFor="dbType">{t('importForm.engine')}</label>
          <select id="dbType" value={dbType} onChange={e => setDbType(e.target.value)}>
            {DESK_DB_ENGINES.map(engine => <option key={engine} value={engine}>{engine}</option>)}
          </select>
        </div>

        <div className="field">
          <label htmlFor="conn">{t('importForm.connectionString')}<span className="req">*</span></label>
          <textarea
            id="conn" value={connectionString} required autoFocus
            placeholder="Host=...;Port=...;Database=...;Username=...;Password=..."
            onChange={e => setConnectionString(e.target.value)}
          />
        </div>

        <div className="notice" style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span>{t('importForm.noDatabase')}</span>
          <button type="button" className="btn btn-sm" style={{ alignSelf: 'flex-start' }}
                  disabled={busy} onClick={onUseGround}>
            {t('importForm.useGround')}
          </button>
        </div>

        <div className="dialog-actions">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>{t('common.cancel')}</button>
          <button type="submit" className="btn btn-primary" disabled={busy || !connectionString.trim()}>
            {busy ? t('importForm.connecting') : t('importForm.connect')}
          </button>
        </div>
      </form>
    </div>
  );
}
