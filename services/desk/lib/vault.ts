import type { DeskSession } from './api';

/**
 * Namines Vault — yedekleme ve geri yükleme istemcisi.
 *
 * Kimlik `lib/api.ts` ile aynı: oturum (JWT) + `projectId`. Bağlantı dizesi
 * ASLA istemciye gelmez; sunucu yetkiyi doğrulayıp şifreli bağlantıyı kendisi
 * çözer.
 *
 * <b>Yedek dosyası tarayıcıya ŞİFRELİ iniyor</b> — çözülmüş hâli veritabanının
 * tamamının düz metin kopyası olurdu ve tarayıcı önbelleğinde, indirilenler
 * klasöründe öyle kalırdı.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export type VaultBackupStatus = 'Running' | 'Succeeded' | 'Failed';
export type VaultBackupKind = 'Manual' | 'PreRestore' | 'Scheduled';
export type VaultCadence = 'Daily' | 'Weekly';

export interface VaultBackup {
  id: string;
  databaseName: string;
  engine: string;
  status: VaultBackupStatus;
  kind: VaultBackupKind;
  sizeBytes: number;
  error: string | null;
  createdAt: string;
  completedAt: string | null;
  store: string;
  /** Yedeğin gerçekten geri yüklenebildiğinin kanıtlandığı an; null ise kanıtlanmadı. */
  verifiedAt: string | null;
  verifyError: string | null;
}

export interface VaultSchedule {
  enabled: boolean;
  cadence: VaultCadence;
  /** Saat UTC — sunucunun saat dilimi değişse de zamanlama kaymasın diye. */
  hourUtc: number;
  dayOfWeek: number | null;
  retainCount: number;
  lastRunAt: string | null;
}

/**
 * Vault'un çalışabilir durumda olup olmadığı.
 *
 * Yedekleme, API'nin bir Docker daemon'una erişmesine bağlı ve bu erişim
 * dağıtıma göre değişiyor. Bunu ekranda söylemezsek, eksikliğin anlaşıldığı
 * ilk an kullanıcının ilk yedek denemesi olur — yani en kötü an.
 */
export interface VaultHealth {
  ok: boolean;
  /** Vault'un yedekleyebildigi motorlar — tek bir motor degil, kayitli saglayicilardan turetilen liste. */
  engines: string[];
  store: string;
  problem: string | null;
}

export interface VaultBackupList {
  /** Yedeklerin fiziksel olarak nerede durduğu — kullanıcı bunu görmeden güvenmemeli. */
  store: string;
  backups: VaultBackup[];
}

export interface VaultRestore {
  id: string;
  backupId: string;
  preRestoreBackupId: string | null;
  status: VaultBackupStatus;
  error: string | null;
  startedAt: string;
  completedAt: string | null;
}

export class VaultError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

async function call(path: string, session: DeskSession, init?: RequestInit): Promise<Response> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${session.token}`, ...(init?.headers ?? {}) },
    cache: 'no-store',
  });

  if (!res.ok) {
    // Sunucunun kendi mesajını taşı: "yedek alınamadı" demek, kullanıcıyı
    // 403 (yetki yok) ile "bu projede canlı bağlantı yok" arasında kör bırakırdı.
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new VaultError(message, res.status);
  }

  return res;
}

export const vaultApi = {
  list: async (session: DeskSession): Promise<VaultBackupList> =>
    (await call(`/api/vault/${encodeURIComponent(session.projectId)}/backups`, session)).json(),

  health: async (session: DeskSession): Promise<VaultHealth> =>
    (await call('/api/vault/health', session)).json(),

  restores: async (session: DeskSession): Promise<VaultRestore[]> =>
    (await call(`/api/vault/${encodeURIComponent(session.projectId)}/restores`, session)).json(),

  /**
   * Yedeklemeyi BASLATIR. Sunucu 202 Accepted donuyor ve is arka planda
   * calisiyor (PERF-003 / B-35) -- yani bu cagri donduginde yedek HENUZ
   * ALINMADI. Bitmesini beklemek icin `status` ile yoklanmali.
   */
  create: async (session: DeskSession): Promise<{ backupId: string; status: string }> =>
    (await call(`/api/vault/${encodeURIComponent(session.projectId)}/backups`, session, {
      method: 'POST',
    })).json(),

  /** Tek bir yedegin durumu. `done` true olunca yoklama biter. */
  status: async (session: DeskSession, backupId: string): Promise<{
    id: string;
    status: string;
    sizeBytes: number;
    errorMessage: string | null;
    createdAt: string;
    completedAt: string | null;
    done: boolean;
  }> =>
    (await call(
      `/api/vault/${encodeURIComponent(session.projectId)}/backups/${encodeURIComponent(backupId)}`,
      session,
    )).json(),

  /**
   * Yedek bitene kadar yoklar.
   *
   * **Neden bir zaman siniri var:** sinirsiz yoklama, sunucu tarafinda asili
   * kalmis bir kayitta sonsuza kadar donen bir arayuz demek. Sinira
   * ulasildiginda is IPTAL EDILMIYOR (sunucuda devam ediyor olabilir) --
   * yalnizca arayuz beklemeyi birakip kullaniciya listeden takip etmesini
   * soyluyor. "Basarisiz" demek yanlis olurdu.
   */
  waitForBackup: async (
    session: DeskSession,
    backupId: string,
    { timeoutMs = 10 * 60 * 1000, intervalMs = 2000 }: { timeoutMs?: number; intervalMs?: number } = {},
  ): Promise<{ done: boolean; status: string; errorMessage: string | null }> => {
    const deadline = Date.now() + timeoutMs;

    for (;;) {
      const snapshot = await vaultApi.status(session, backupId);
      if (snapshot.done) {
        return { done: true, status: snapshot.status, errorMessage: snapshot.errorMessage };
      }
      if (Date.now() >= deadline) {
        return { done: false, status: snapshot.status, errorMessage: null };
      }
      await new Promise(resolve => setTimeout(resolve, intervalMs));
    }
  },

  remove: async (session: DeskSession, backupId: string): Promise<void> => {
    await call(
      `/api/vault/${encodeURIComponent(session.projectId)}/backups/${encodeURIComponent(backupId)}`,
      session, { method: 'DELETE' },
    );
  },

  /**
   * Geri yükleme. `confirmDatabaseName` kullanıcının ELİYLE yazdığı ad —
   * sunucu birebir eşleşmiyorsa işlemi hiç başlatmıyor.
   */
  restore: async (
    session: DeskSession, backupId: string, confirmDatabaseName: string,
  ): Promise<{ restored: boolean; preRestoreBackupId: string | null }> =>
    (await call(
      `/api/vault/${encodeURIComponent(session.projectId)}/backups/${encodeURIComponent(backupId)}/restore`,
      session,
      { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ confirmDatabaseName }) },
    )).json(),

  verify: async (session: DeskSession, backupId: string): Promise<{ verified: boolean; verifiedAt: string }> =>
    (await call(
      `/api/vault/${encodeURIComponent(session.projectId)}/backups/${encodeURIComponent(backupId)}/verify`,
      session, { method: 'POST' },
    )).json(),

  schedule: async (session: DeskSession): Promise<VaultSchedule> =>
    (await call(`/api/vault/${encodeURIComponent(session.projectId)}/schedule`, session)).json(),

  saveSchedule: async (session: DeskSession, schedule: VaultSchedule): Promise<VaultSchedule> =>
    (await call(`/api/vault/${encodeURIComponent(session.projectId)}/schedule`, session, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        enabled: schedule.enabled,
        cadence: schedule.cadence,
        hourUtc: schedule.hourUtc,
        dayOfWeek: schedule.dayOfWeek,
        retainCount: schedule.retainCount,
      }),
    })).json(),

  download: async (session: DeskSession, backupId: string): Promise<{ blob: Blob; fileName: string }> => {
    const res = await call(
      `/api/vault/${encodeURIComponent(session.projectId)}/backups/${encodeURIComponent(backupId)}/download`,
      session,
    );
    const disposition = res.headers.get('content-disposition') ?? '';
    const match = /filename="?([^"]+)"?/.exec(disposition);
    return { blob: await res.blob(), fileName: match?.[1] ?? `${backupId}.nvlt` };
  },
};

/**
 * İki zaman damgası arasındaki süre. Bitmemiş bir iş için `null`.
 *
 * Yedeğin NE KADAR SÜRDÜĞÜ, boyutu kadar önemli bir sinyal: aniden uzayan bir
 * yedek, kullanıcının fark etmesi gereken bir sorunun ilk işareti olur.
 */
export function formatDuration(startedAt: string, completedAt: string | null): string | null {
  if (!completedAt) return null;

  const ms = new Date(completedAt).getTime() - new Date(startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return null;

  if (ms < 1000) return `${ms} ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(1)} sn`;

  const minutes = Math.floor(seconds / 60);
  return `${minutes} dk ${Math.round(seconds % 60)} sn`;
}
