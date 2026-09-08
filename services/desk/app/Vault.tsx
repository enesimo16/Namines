'use client';

import { useCallback, useEffect, useState } from 'react';
import { type DeskSession } from '../lib/api';
import {
  vaultApi, VaultError, formatSize, formatDuration,
  type VaultBackup, type VaultRestore,
} from '../lib/vault';
import PageHead from './PageHead';

/**
 * Namines Vault — yedekleme ekranı.
 *
 * <b>Geri yükleme, Desk'teki en yıkıcı işlem:</b> hedef veritabanının mevcut
 * nesnelerini siler. Bu yüzden tek tıkla tetiklenmiyor — kullanıcı veritabanının
 * adını ELİYLE yazmak zorunda. Aynı gerekçeyle sunucu her geri yüklemeden önce
 * ZORUNLU bir ön yedek alıyor; ekran bunu da açıkça söylüyor.
 *
 * Yetki Desk'te değil sunucuda kararlaştırılıyor (Desk'in kendi yetki kararı
 * yok). `isOwner` burada yalnızca yıkıcı düğmeleri GİZLEMEK için — sunucu
 * kontrolünün yerine geçmez, onu tekrar eder.
 */
export default function Vault({ session, isOwner }: { session: DeskSession; isOwner: boolean }) {
  const [backups, setBackups] = useState<VaultBackup[] | null>(null);
  const [restores, setRestores] = useState<VaultRestore[]>([]);
  const [store, setStore] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [rowBusyId, setRowBusyId] = useState<string | null>(null);

  // Onay kutusu: hangi yedek ve kullanıcının o ana kadar yazdığı ad.
  const [confirming, setConfirming] = useState<{ backup: VaultBackup; typed: string } | null>(null);

  const reload = useCallback(async () => {
    try {
      const [list, history] = await Promise.all([vaultApi.list(session), vaultApi.restores(session)]);
      setBackups(list.backups);
      setStore(list.store);
      setRestores(history);
      setError(null);
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Yedekler okunamadı.');
      setBackups([]);
    }
  }, [session]);

  useEffect(() => { reload(); }, [reload]);

  async function handleCreate() {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await vaultApi.create(session);
      setNotice('Yedek alındı.');
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Yedek alınamadı.');
    } finally {
      setBusy(false);
    }
  }

  async function handleDownload(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    try {
      const { blob, fileName } = await vaultApi.download(session, backup.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      // Nesne URL'i serbest bırakılmazsa blob sekme kapanana kadar bellekte kalır.
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'İndirilemedi.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleDelete(backup: VaultBackup) {
    if (!confirm(`${new Date(backup.createdAt).toLocaleString('tr-TR')} tarihli yedek kalıcı olarak silinecek. Emin misiniz?`)) return;
    setRowBusyId(backup.id);
    setError(null);
    try {
      await vaultApi.remove(session, backup.id);
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Silinemedi.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleRestore() {
    if (!confirming) return;

    const { backup, typed } = confirming;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await vaultApi.restore(session, backup.id, typed);
      setConfirming(null);
      setNotice(
        result.preRestoreBackupId
          ? 'Geri yükleme tamamlandı. Öncesindeki hâl ayrı bir yedek olarak saklandı.'
          : 'Geri yükleme tamamlandı.',
      );
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Geri yüklenemedi.');
    } finally {
      setBusy(false);
    }
  }

  const confirmMatches = confirming?.typed.trim() === confirming?.backup.databaseName;

  return (
    <div className="page">
      <PageHead
        title="Yedekler"
        desc={<>
          Projenin canlı veritabanının tam kopyası. Dosyalar <b>şifreli</b> saklanır ve
          tarayıcıya da şifreli iner. <b>Geri yükleme, hedefteki veriyi siler</b> — bu yüzden
          her geri yüklemeden önce mevcut hâlin yedeği otomatik alınır.
        </>}
        actions={
          <button className="btn btn-primary" disabled={busy} onClick={handleCreate}>
            {busy ? 'Çalışıyor…' : 'Şimdi yedek al'}
          </button>
        }
      />

      {error && <div className="notice notice-error">{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      {/* Yedeklerin NEREDE durduğu gizlenecek bir ayrıntı değil: v1 sunucu
          diskini kullanıyor ve kullanıcı bunu bilmeden yedeğe güvenmemeli. */}
      {store && <div className="notice">Yedekler burada saklanıyor: <code>{store}</code></div>}

      {confirming && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>Bu işlem geri alınamaz.</b>
          <span>
            <code>{confirming.backup.databaseName}</code> veritabanındaki mevcut veriler silinip{' '}
            {new Date(confirming.backup.createdAt).toLocaleString('tr-TR')} tarihli yedekle
            değiştirilecek. Devam etmek için veritabanının adını yazın.
          </span>
          <input
            type="text"
            value={confirming.typed}
            placeholder={confirming.backup.databaseName}
            style={{ maxWidth: 280 }}
            onChange={e => setConfirming({ ...confirming, typed: e.target.value })}
          />
          <div className="row-actions">
            <button className="btn btn-danger" disabled={busy || !confirmMatches} onClick={handleRestore}>
              {busy ? 'Geri yükleniyor…' : 'Geri yükle'}
            </button>
            <button className="btn btn-sm" disabled={busy} onClick={() => setConfirming(null)}>
              Vazgeç
            </button>
          </div>
        </div>
      )}

      {!backups ? (
        <div className="empty">Yükleniyor…</div>
      ) : backups.length === 0 ? (
        <div className="empty">Bu projenin henüz bir yedeği yok.</div>
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>Tarih</th><th>Tür</th><th>Motor</th><th>Boyut</th>
                <th>Süre</th><th>Durum</th><th style={{ textAlign: 'right' }}>İşlem</th>
              </tr>
            </thead>
            <tbody>
              {backups.map(b => (
                <tr key={b.id}>
                  <td>{new Date(b.createdAt).toLocaleString('tr-TR')}</td>
                  <td>{b.kind === 'PreRestore' ? 'Geri yükleme öncesi' : 'Elle'}</td>
                  <td>{b.engine}</td>
                  <td>{b.status === 'Succeeded' ? formatSize(b.sizeBytes) : '—'}</td>
                  <td>{formatDuration(b.createdAt, b.completedAt) ?? '—'}</td>
                  <td>
                    {b.status === 'Succeeded' ? 'Tamam'
                      : b.status === 'Running' ? 'Alınıyor…'
                      // Hata metni satırın içinde: kullanıcı "neden başarısız"
                      // sorusunu sormak için başka bir ekrana gitmek zorunda kalmasın.
                      : <span title={b.error ?? undefined}>Başarısız</span>}
                  </td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {b.status === 'Succeeded' && isOwner && (
                        <>
                          <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                  onClick={() => handleDownload(b)}>
                            İndir
                          </button>
                          <button className="btn btn-sm btn-danger" disabled={busy}
                                  onClick={() => setConfirming({ backup: b, typed: '' })}>
                            Geri yükle
                          </button>
                          <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                  onClick={() => handleDelete(b)}>
                            Sil
                          </button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {restores.length > 0 && (
        <>
          <PageHead
            title="Geri yükleme geçmişi"
            desc="Bu veritabanına en son kimin, ne zaman, hangi yedeği yazdığı."
          />
          <div className="grid-wrap">
            <table>
              <thead>
                <tr><th>Tarih</th><th>Kaynak yedek</th><th>Süre</th><th>Durum</th></tr>
              </thead>
              <tbody>
                {restores.map(r => (
                  <tr key={r.id}>
                    <td>{new Date(r.startedAt).toLocaleString('tr-TR')}</td>
                    <td><code>{r.backupId.slice(0, 8)}</code></td>
                    <td>{formatDuration(r.startedAt, r.completedAt) ?? '—'}</td>
                    <td>
                      {r.status === 'Succeeded' ? 'Tamam'
                        : r.status === 'Running' ? 'Sürüyor…'
                        : <span title={r.error ?? undefined}>Başarısız</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
