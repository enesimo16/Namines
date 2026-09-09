'use client';

import { useCallback, useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { formatSize } from '../lib/format';
import {
  groundApi, GroundError, daysUntilPurge,
  type GroundDatabase, type GroundProvider, type GroundMetrics,
} from '../lib/ground';
import PageHead from './PageHead';

/**
 * Namines Ground — yönetilen veritabanı ekranı.
 *
 * <b>Ground'un kendi sitesi yok</b>; bir arka uç modülü ve kullanıcıya bakan
 * tek yüzü bu görünüm. Gerekçesi `namines-ground/02-V1-KARARLARI.md`'de.
 *
 * <b>Silme, Desk'teki en yıkıcı ikinci işlem</b> (Vault'un geri yüklemesinden
 * sonra): veritabanının tamamını yok eder. Bu yüzden proje adı elle yazılıyor
 * ve silme hemen değil, geri alınabilir bir pencereyle yapılıyor.
 */
export default function Ground({ session, isOwner }: { session: DeskSession; isOwner: boolean }) {
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [database, setDatabase] = useState<GroundDatabase | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmText, setConfirmText] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [metrics, setMetrics] = useState<GroundMetrics | null>(null);

  const reload = useCallback(async () => {
    try {
      const [list, record] = await Promise.all([
        groundApi.providers(session), groundApi.get(session),
      ]);
      setProviders(list);
      setDatabase(record);
      setError(null);

      // Ölçüm AYRI çağrı ve hatası yutuluyor: sağlayıcıya gerçek bir sorgu
      // gidiyor, oradaki bir aksaklık ekranın tamamını çökertmemeli.
      if (record && record.status !== 'Deleted') {
        try {
          setMetrics(await groundApi.metrics(session));
        } catch {
          setMetrics(null);
        }
      } else {
        setMetrics(null);
      }
    } catch (err) {
      setError(err instanceof GroundError ? err.message : 'Yönetilen veritabanı bilgisi okunamadı.');
      setProviders([]);
    }
  }, [session]);

  useEffect(() => { reload(); }, [reload]);

  async function run(action: () => Promise<unknown>, success: string) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
      setNotice(success);
      await reload();
    } catch (err) {
      setError(err instanceof GroundError ? err.message : 'İşlem başarısız oldu.');
    } finally {
      setBusy(false);
    }
  }

  const handleProvision = (provider: string) =>
    run(() => groundApi.provision(session, provider), 'Yönetilen veritabanı açıldı.');

  const handleDelete = () =>
    run(async () => {
      await groundApi.requestDelete(session, confirmText);
      setConfirming(false);
      setConfirmText('');
    }, 'Silme istendi. Bekleme penceresi içinde geri alabilirsiniz.');

  const handleCancelDelete = () =>
    run(() => groundApi.cancelDelete(session), 'Silme geri alındı, veritabanı yeniden kullanılabilir.');

  const remainingDays = database?.deleteRequestedAt
    ? daysUntilPurge(database.deleteRequestedAt, database.graceDays)
    : null;

  return (
    <div className="page">
      <PageHead
        title="Barındırma"
        desc={<>
          Bu proje için <b>yönetilen bir PostgreSQL veritabanı</b> açar. Bağlantı sunucuda
          şifreli saklanır ve tarayıcıya hiç gelmez. <b>Silme geri alınabilir bir bekleme
          penceresiyle</b> yapılır — yanlışlıkla silinen bir veritabanı, çoğu zaman ancak
          birileri onu kullanmayı denediğinde fark edilir.
        </>}
      />

      {error && <div className="notice notice-error">{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      {!isOwner && (
        <div className="notice">
          Yönetilen veritabanı açmak ve silmek yalnızca proje sahibinin (Owner) yetkisindedir.
        </div>
      )}

      {/* Yalnizca UYARI: veritabani kisitlanmiyor, kapatilmiyor. Kullanici
          "neden verime erisemiyorum" diye sormaz -- yalnizca "bunu bilmelisin"
          diyoruz. */}
      {metrics?.storageWarning && (
        <div className="notice">{metrics.storageWarning}</div>
      )}

      {database && database.status !== 'Deleted' ? (
        <div className="grid-wrap">
          <table>
            <tbody>
              <tr><th style={{ width: 200 }}>Durum</th><td>{STATUS_LABELS[database.status]}</td></tr>
              <tr><th>Sağlayıcı</th><td>{database.provider}</td></tr>
              <tr>
                <th>Sağlayıcıdaki adı</th>
                {/* Kaynağı sağlayıcının kendi panelinde bulabilmek için. */}
                <td><code>{database.providerProjectId ?? '—'}</code></td>
              </tr>
              <tr><th>Bölge</th><td>{database.region ?? '—'}</td></tr>
              <tr><th>Açılış</th><td>{new Date(database.createdAt).toLocaleString('tr-TR')}</td></tr>
              {/* null = BILINMIYOR, sifir degil. "0 B" yazmak bos bir
                  veritabani izlenimi verirdi. */}
              <tr>
                <th>Kullanılan alan</th>
                <td>{metrics?.storageBytes != null ? formatSize(metrics.storageBytes) : '—'}</td>
              </tr>
              <tr>
                <th>Açık bağlantı</th>
                <td>{metrics?.activeConnections ?? '—'}</td>
              </tr>
              {database.error && (
                <tr><th>Hata</th><td>{database.error}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      ) : null}

      {database?.status === 'PendingDelete' && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>Bu veritabanı silinmeyi bekliyor.</b>
          <span>
            {remainingDays === 0
              ? 'Bekleme penceresi doldu; ilk temizlik turunda kalıcı olarak silinecek.'
              : `Kalıcı olarak silinmesine ${remainingDays} gün kaldı. O ana kadar geri alabilirsiniz.`}
          </span>
          {isOwner && (
            <button className="btn" style={{ alignSelf: 'flex-start' }} disabled={busy}
                    onClick={handleCancelDelete}>
              {busy ? 'Geri alınıyor…' : 'Silmeyi geri al'}
            </button>
          )}
        </div>
      )}

      {database?.status === 'Active' && isOwner && (
        confirming ? (
          <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <b>Bu veritabanı ve içindeki tüm veriler silinecek.</b>
            <span>
              Silme hemen olmaz; bekleme penceresi boyunca geri alabilirsiniz. Devam etmek
              için proje adını yazın.
            </span>
            <input type="text" value={confirmText} style={{ maxWidth: 280 }}
                   onChange={e => setConfirmText(e.target.value)} />
            <div className="row-actions">
              <button className="btn btn-danger" disabled={busy || !confirmText.trim()}
                      onClick={handleDelete}>
                {busy ? 'İsteniyor…' : 'Silmeyi başlat'}
              </button>
              <button className="btn btn-sm" disabled={busy}
                      onClick={() => { setConfirming(false); setConfirmText(''); }}>
                Vazgeç
              </button>
            </div>
          </div>
        ) : (
          <div className="row-actions">
            <button className="btn btn-danger btn-sm" onClick={() => setConfirming(true)}>
              Veritabanını sil
            </button>
          </div>
        )
      )}

      {!database || database.status === 'Deleted' ? (
        <>
          <PageHead
            title="Sağlayıcılar"
            desc="Veritabanının nerede açılacağını seçin. Her sağlayıcının sorumluluğu farklıdır."
          />

          {!providers ? (
            <div className="empty">Yükleniyor…</div>
          ) : providers.length === 0 ? (
            <EmptyState
              title="Kayıtlı sağlayıcı yok"
              description={
                <>
                  Ground, projeniz için yönetilen bir veritabanı açar. Sunucuda hiçbir sağlayıcı
                  yapılandırılmamış — yöneticinizin <code>Ground__*</code> ayarlarını tanımlaması
                  gerekiyor.
                </>
              }
            />
          ) : (
            <div className="grid-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Sağlayıcı</th><th>Durum</th><th>Sorumluluk</th>
                    <th style={{ textAlign: 'right' }}>İşlem</th>
                  </tr>
                </thead>
                <tbody>
                  {providers.map(p => (
                    <tr key={p.name}>
                      <td>{p.name}</td>
                      <td>
                        {/* Uc ayri hal: hazir / yapilandirilmamis / denenmemis.
                            "Canli kanitlanmadi" gizlenirse, kullanici verisini
                            denenmemis bir yola koydugunu bilmezdi. */}
                        {p.problem
                          ? <span title={p.problem}>Yapılandırılmamış</span>
                          : p.liveVerified
                            ? 'Hazır'
                            : <span title="Bu sağlayıcı gerçek bir hesaba karşı hiç denenmedi.">
                                ⚠ Canlı denenmedi
                              </span>}
                      </td>
                      <td style={{ maxWidth: 420, fontSize: 12.5 }}>{p.responsibility}</td>
                      <td>
                        <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                          <button
                            className="btn btn-sm"
                            disabled={busy || !isOwner || p.problem !== null}
                            title={p.problem ?? undefined}
                            onClick={() => handleProvision(p.name)}
                          >
                            {busy ? 'Açılıyor…' : 'Bu sağlayıcıda aç'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      ) : null}
    </div>
  );
}

const STATUS_LABELS: Record<GroundDatabase['status'], string> = {
  Provisioning: 'Açılıyor…',
  Active: 'Etkin',
  PendingDelete: 'Silinmeyi bekliyor',
  Deleted: 'Silindi',
  Failed: 'Başarısız',
};
