'use client';

import { useCallback, useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { formatSize } from '../lib/format';
import {
  vaultApi, VaultError, formatDuration,
  type VaultBackup, type VaultRestore, type VaultSchedule, type VaultHealth,
} from '../lib/vault';
import PageHead from './PageHead';

/** Haftalık zamanlamada gün adları — dizideki sıra sunucudaki 0=Pazar ile aynı. */
const DAY_NAMES = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi'];

const HOURS = Array.from({ length: 24 }, (_, hour) => hour);

const KIND_LABELS: Record<VaultBackup['kind'], string> = {
  Manual: 'Elle',
  Scheduled: 'Otomatik',
  PreRestore: 'Geri yükleme öncesi',
};

/**
 * Namines Vault — yedekleme ekranı.
 *
 * <b>Vault'un kendi sitesi yok</b>: bir arka uç modülü ve kullanıcıya bakan tek
 * yüzü bu görünüm. Gerekçesi ve deploy şartları
 * <c>namines-vault/02-ERISIM-VE-DEPLOY.md</c>'de.
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
  const [schedule, setSchedule] = useState<VaultSchedule | null>(null);
  const [savingSchedule, setSavingSchedule] = useState(false);
  const [health, setHealth] = useState<VaultHealth | null>(null);

  // Onay kutusu: hangi yedek ve kullanıcının o ana kadar yazdığı ad.
  const [confirming, setConfirming] = useState<{ backup: VaultBackup; typed: string } | null>(null);

  const reload = useCallback(async () => {
    try {
      const [list, history, plan, status] = await Promise.all([
        vaultApi.list(session), vaultApi.restores(session),
        vaultApi.schedule(session), vaultApi.health(session),
      ]);
      setBackups(list.backups);
      setStore(list.store);
      setRestores(history);
      setSchedule(plan);
      setHealth(status);
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

  async function handleVerify(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    setNotice(null);
    try {
      await vaultApi.verify(session, backup.id);
      setNotice('Yedek doğrulandı: geçici bir sunucuya geri yüklenip çalıştığı kanıtlandı.');
    } catch (err) {
      // Doğrulamanın BAŞARISIZ olması bir arayüz hatası değil, gerçek bir bulgu:
      // yedek bozuk. Bu yüzden mesaj bastırılmıyor, listeye de yazılıyor.
      setError(err instanceof VaultError ? err.message : 'Doğrulanamadı.');
    } finally {
      setRowBusyId(null);
      await reload();
    }
  }

  async function handleSaveSchedule(next: VaultSchedule) {
    setSavingSchedule(true);
    setError(null);
    try {
      setSchedule(await vaultApi.saveSchedule(session, next));
      setNotice('Otomatik yedek ayarı kaydedildi.');
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Ayar kaydedilemedi.');
    } finally {
      setSavingSchedule(false);
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
          <button
            className="btn btn-primary"
            disabled={busy || health?.ok === false}
            title={health?.problem ?? undefined}
            onClick={handleCreate}
          >
            {busy ? 'Çalışıyor…' : 'Şimdi yedek al'}
          </button>
        }
      />

      {/* Engel varsa EN USTTE: kullanici "neden calismiyor" diye tahmin
          yurutmek zorunda kalmasin. */}
      {health && !health.ok && (
        <div className="notice notice-error">{health.problem}</div>
      )}

      {error && <div className="notice notice-error">{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      {/* Yedeklerin NEREDE durduğu gizlenecek bir ayrıntı değil: v1 sunucu
          diskini kullanıyor ve kullanıcı bunu bilmeden yedeğe güvenmemeli. */}
      {store && <div className="notice">Yedekler burada saklanıyor: <code>{store}</code></div>}

      {/* Hangi motorların yedeklenebildiği ekranda YAZIYOR: aksi halde
          desteklenmeyen bir motorda kullanıcı bunu ancak ilk yedek denemesi
          hataya düştüğünde öğrenirdi. Liste sunucudaki kayıtlı
          sağlayıcılardan geliyor, burada elle tutulmuyor. */}
      {health && health.engines.length > 0 && (
        <div className="notice">Yedeklenebilen motorlar: <code>{health.engines.join(', ')}</code></div>
      )}

      {schedule && (
        <div className="grid-wrap" style={{ padding: 10, display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'flex-end' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
            <input
              type="checkbox"
              checked={schedule.enabled}
              disabled={!isOwner || savingSchedule}
              onChange={e => handleSaveSchedule({ ...schedule, enabled: e.target.checked })}
            />
            Otomatik yedek
          </label>

          <div className="field" style={{ margin: 0 }}>
            <label htmlFor="vault-cadence">Sıklık</label>
            <select
              id="vault-cadence"
              value={schedule.cadence}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({
                ...schedule,
                cadence: e.target.value as typeof schedule.cadence,
                // Haftalığa geçerken bir gün seçili olmak zorunda; sunucu
                // günsüz haftalık ayarı zaten reddediyor.
                dayOfWeek: e.target.value === 'Weekly' ? (schedule.dayOfWeek ?? 0) : null,
              })}
            >
              <option value="Daily">Her gün</option>
              <option value="Weekly">Haftada bir</option>
            </select>
          </div>

          {schedule.cadence === 'Weekly' && (
            <div className="field" style={{ margin: 0 }}>
              <label htmlFor="vault-day">Gün</label>
              <select
                id="vault-day"
                value={schedule.dayOfWeek ?? 0}
                disabled={!isOwner || savingSchedule}
                onChange={e => setSchedule({ ...schedule, dayOfWeek: Number(e.target.value) })}
              >
                {DAY_NAMES.map((name, index) => <option key={name} value={index}>{name}</option>)}
              </select>
            </div>
          )}

          <div className="field" style={{ margin: 0 }}>
            {/* Saat UTC: sunucunun saat dilimi değişse de zamanlama kaymasın. */}
            <label htmlFor="vault-hour">Saat (UTC)</label>
            <select
              id="vault-hour"
              value={schedule.hourUtc}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({ ...schedule, hourUtc: Number(e.target.value) })}
            >
              {HOURS.map(h => <option key={h} value={h}>{String(h).padStart(2, '0')}:00</option>)}
            </select>
          </div>

          <div className="field" style={{ margin: 0 }}>
            <label htmlFor="vault-retain">Saklanacak yedek</label>
            <input
              id="vault-retain" type="number" min={1} max={60} style={{ width: 80 }}
              value={schedule.retainCount}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({ ...schedule, retainCount: Number(e.target.value) })}
            />
          </div>

          <button className="btn" disabled={!isOwner || savingSchedule}
                  onClick={() => handleSaveSchedule(schedule)}>
            {savingSchedule ? 'Kaydediliyor…' : 'Ayarı kaydet'}
          </button>

          <span style={{ fontSize: 12.5, opacity: 0.75 }}>
            {/* Saklama politikasının YALNIZCA otomatik yedekleri sildiğini söylemek
                şart: kullanıcı elle aldığı yedeğin de silineceğini sanmasın. */}
            Saklama sayısı yalnızca otomatik yedekleri sınırlar; elle aldıklarınıza dokunulmaz.
            {schedule.lastRunAt && ` Son otomatik yedek: ${new Date(schedule.lastRunAt).toLocaleString('tr-TR')}.`}
          </span>
        </div>
      )}

      {confirming && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>Bu işlem geri alınamaz.</b>
          <span>
            <code>{confirming.backup.databaseName}</code> veritabanındaki mevcut veriler silinip{' '}
            {new Date(confirming.backup.createdAt).toLocaleString('tr-TR')} tarihli yedekle
            değiştirilecek. Devam etmek için veritabanının adını yazın.
          </span>
          <input
            aria-label="Onay için veritabanı adı"
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
        <EmptyState
          title="Henüz yedek alınmadı"
          description={
            <>
              Yedekler şifrelenerek saklanır ve geri yüklenebilirlikleri temiz bir sunucuya
              gerçekten geri yüklenerek <strong>kanıtlanır</strong>. Yukarıdaki
              &quot;Şimdi yedek al&quot; düğmesiyle ilkini oluşturun, ya da düzenli yedek için
              zamanlamayı açın.
            </>
          }
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>Tarih</th><th>Tür</th><th>Motor</th><th>Boyut</th>
                <th>Süre</th><th>Durum</th><th>Doğrulama</th><th style={{ textAlign: 'right' }}>İşlem</th>
              </tr>
            </thead>
            <tbody>
              {backups.map(b => (
                <tr key={b.id}>
                  <td>{new Date(b.createdAt).toLocaleString('tr-TR')}</td>
                  <td>{KIND_LABELS[b.kind]}</td>
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
                    {/* Üç ayrı hâl: kanıtlandı / bozuk çıktı / henüz denenmedi.
                        Son ikisini birleştirmek, bozuk bir yedeği "sadece
                        doğrulanmamış" gibi gösterirdi. */}
                    {b.verifiedAt ? (
                      <span title={`Doğrulandı: ${new Date(b.verifiedAt).toLocaleString('tr-TR')}`}>✓ Doğrulandı</span>
                    ) : b.verifyError ? (
                      <span title={b.verifyError}>✗ Bozuk</span>
                    ) : '—'}
                  </td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {/* Doğrulama geçici bir sunucuda yapılıyor, projenin
                          veritabanına dokunmuyor — bu yüzden Owner şartı yok. */}
                      {b.status === 'Succeeded' && (
                        <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                onClick={() => handleVerify(b)}>
                          {rowBusyId === b.id ? 'Deneniyor…' : 'Doğrula'}
                        </button>
                      )}
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
