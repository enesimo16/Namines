import api from './api';

/**
 * Namines Vault ve Namines Ground için istemci — bu ikisinin artık Desk'e
 * bağlı olmayan, /vault ve /ground altında bağımsız sayfaları var. Kimlik
 * `services/api.ts`'teki `api` örneğiyle aynı: httpOnly çerez + CSRF başlığı
 * (Desk'in ayrı mikroservisi Bearer jetonu kullanıyor çünkü farklı origin'de
 * çalışıyor — burası ana uygulamanın kendisi, aynı oturumu doğrudan kullanır).
 *
 * Bağlantı dizesi burada da ASLA görünmez: sunucu yetkiyi doğrulayıp şifreli
 * bağlantıyı kendi tarafında çözer.
 */

export class VaultGroundError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

function errorMessage(err: unknown, fallback: string): never {
  const status = (err as { response?: { status?: number; data?: { error?: string; message?: string } } })?.response?.status ?? 0;
  const body = (err as { response?: { data?: { error?: string; message?: string } } })?.response?.data;
  throw new VaultGroundError(body?.error ?? body?.message ?? fallback, status);
}

// ── Vault ────────────────────────────────────────────────────────────────

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
  verifiedAt: string | null;
  verifyError: string | null;
}

export interface VaultSchedule {
  enabled: boolean;
  cadence: VaultCadence;
  hourUtc: number;
  dayOfWeek: number | null;
  retainCount: number;
  lastRunAt: string | null;
}

export interface VaultHealth {
  ok: boolean;
  engines: string[];
  store: string;
  problem: string | null;
}

export interface VaultBackupList {
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

export const vaultApi = {
  health: async (): Promise<VaultHealth> => {
    try { return (await api.get('/vault/health')).data; }
    catch (err) { errorMessage(err, 'Could not load Vault status.'); }
  },

  list: async (projectId: string): Promise<VaultBackupList> => {
    try { return (await api.get(`/vault/${encodeURIComponent(projectId)}/backups`)).data; }
    catch (err) { errorMessage(err, 'Could not load backups.'); }
  },

  restores: async (projectId: string): Promise<VaultRestore[]> => {
    try { return (await api.get(`/vault/${encodeURIComponent(projectId)}/restores`)).data; }
    catch (err) { errorMessage(err, 'Could not load restore history.'); }
  },

  /** Sunucu 202 döner; yedek arka planda alınır — `waitForBackup` ile yoklanmalı. */
  create: async (projectId: string): Promise<{ backupId: string; status: string }> => {
    try { return (await api.post(`/vault/${encodeURIComponent(projectId)}/backups`)).data; }
    catch (err) { errorMessage(err, 'Backup failed.'); }
  },

  status: async (projectId: string, backupId: string): Promise<{
    id: string; status: string; sizeBytes: number; errorMessage: string | null;
    createdAt: string; completedAt: string | null; done: boolean;
  }> => {
    try {
      return (await api.get(
        `/vault/${encodeURIComponent(projectId)}/backups/${encodeURIComponent(backupId)}`,
      )).data;
    } catch (err) { errorMessage(err, 'Could not load backup status.'); }
  },

  waitForBackup: async (
    projectId: string, backupId: string,
    { timeoutMs = 10 * 60 * 1000, intervalMs = 2000 }: { timeoutMs?: number; intervalMs?: number } = {},
  ): Promise<{ done: boolean; status: string; errorMessage: string | null }> => {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      const snapshot = await vaultApi.status(projectId, backupId);
      if (snapshot.done) return { done: true, status: snapshot.status, errorMessage: snapshot.errorMessage };
      if (Date.now() >= deadline) return { done: false, status: snapshot.status, errorMessage: null };
      await new Promise(resolve => setTimeout(resolve, intervalMs));
    }
  },

  remove: async (projectId: string, backupId: string): Promise<void> => {
    try {
      await api.delete(`/vault/${encodeURIComponent(projectId)}/backups/${encodeURIComponent(backupId)}`);
    } catch (err) { errorMessage(err, 'Delete failed.'); }
  },

  restore: async (
    projectId: string, backupId: string, confirmDatabaseName: string,
  ): Promise<{ restored: boolean; preRestoreBackupId: string | null }> => {
    try {
      return (await api.post(
        `/vault/${encodeURIComponent(projectId)}/backups/${encodeURIComponent(backupId)}/restore`,
        { confirmDatabaseName },
      )).data;
    } catch (err) { errorMessage(err, 'Restore failed.'); }
  },

  verify: async (projectId: string, backupId: string): Promise<{ verified: boolean; verifiedAt: string }> => {
    try {
      return (await api.post(
        `/vault/${encodeURIComponent(projectId)}/backups/${encodeURIComponent(backupId)}/verify`,
      )).data;
    } catch (err) { errorMessage(err, 'Verification failed.'); }
  },

  schedule: async (projectId: string): Promise<VaultSchedule> => {
    try { return (await api.get(`/vault/${encodeURIComponent(projectId)}/schedule`)).data; }
    catch (err) { errorMessage(err, 'Could not load the schedule.'); }
  },

  saveSchedule: async (projectId: string, schedule: VaultSchedule): Promise<VaultSchedule> => {
    try {
      return (await api.put(`/vault/${encodeURIComponent(projectId)}/schedule`, {
        enabled: schedule.enabled,
        cadence: schedule.cadence,
        hourUtc: schedule.hourUtc,
        dayOfWeek: schedule.dayOfWeek,
        retainCount: schedule.retainCount,
      })).data;
    } catch (err) { errorMessage(err, 'Could not save the setting.'); }
  },

  download: async (projectId: string, backupId: string): Promise<{ blob: Blob; fileName: string }> => {
    try {
      const res = await api.get(
        `/vault/${encodeURIComponent(projectId)}/backups/${encodeURIComponent(backupId)}/download`,
        { responseType: 'blob' },
      );
      const disposition = res.headers['content-disposition'] ?? '';
      const match = /filename="?([^"]+)"?/.exec(disposition);
      return { blob: res.data, fileName: match?.[1] ?? `${backupId}.nvlt` };
    } catch (err) { errorMessage(err, 'Download failed.'); }
  },
};

export function formatDuration(startedAt: string, completedAt: string | null): string | null {
  if (!completedAt) return null;
  const ms = new Date(completedAt).getTime() - new Date(startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return null;
  if (ms < 1000) return `${ms} ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(1)} s`;
  const minutes = Math.floor(seconds / 60);
  return `${minutes}m ${Math.round(seconds % 60)}s`;
}

// ── Ground ───────────────────────────────────────────────────────────────

export type GroundStatus = 'Provisioning' | 'Active' | 'PendingDelete' | 'Deleted' | 'Failed';

export interface GroundProvider {
  name: string;
  liveVerified: boolean;
  supportsBranching: boolean;
  supportsRegionChoice: boolean;
  responsibility: string;
  problem: string | null;
}

export interface GroundDatabase {
  id: string;
  projectId: string;
  provider: string;
  providerProjectId: string | null;
  region: string | null;
  status: GroundStatus;
  error: string | null;
  createdAt: string;
  deleteRequestedAt: string | null;
  deletedAt: string | null;
  graceDays: number;
}

export interface GroundMetrics {
  storageBytes: number | null;
  activeConnections: number | null;
  storageWarning: string | null;
}

export const groundApi = {
  providers: async (): Promise<GroundProvider[]> => {
    try { return (await api.get('/ground/providers')).data; }
    catch (err) { errorMessage(err, 'Could not load providers.'); }
  },

  get: async (projectId: string): Promise<GroundDatabase | null> => {
    try {
      const res = await api.get(`/ground/${encodeURIComponent(projectId)}`, { validateStatus: s => s === 200 || s === 204 });
      return res.status === 204 ? null : res.data;
    } catch (err) { errorMessage(err, 'Could not load the managed database.'); }
  },

  metrics: async (projectId: string): Promise<GroundMetrics> => {
    try { return (await api.get(`/ground/${encodeURIComponent(projectId)}/metrics`)).data; }
    catch (err) { errorMessage(err, 'Could not load metrics.'); }
  },

  provision: async (projectId: string, provider: string): Promise<GroundDatabase> => {
    try { return (await api.post(`/ground/${encodeURIComponent(projectId)}/provision`, { provider })).data; }
    catch (err) { errorMessage(err, 'Could not create the database.'); }
  },

  requestDelete: async (projectId: string, confirmProjectName: string): Promise<GroundDatabase> => {
    try {
      return (await api.post(`/ground/${encodeURIComponent(projectId)}/delete`, { confirmProjectName })).data;
    } catch (err) { errorMessage(err, 'Delete request failed.'); }
  },

  cancelDelete: async (projectId: string): Promise<GroundDatabase> => {
    try { return (await api.post(`/ground/${encodeURIComponent(projectId)}/cancel-delete`)).data; }
    catch (err) { errorMessage(err, 'Could not cancel the deletion.'); }
  },
};

/** Bekleme penceresi dolana kadar kaç gün kaldığı. */
export function daysUntilPurge(deleteRequestedAt: string, graceDays: number): number {
  const deadline = new Date(deleteRequestedAt).getTime() + graceDays * 24 * 60 * 60 * 1000;
  return Math.max(0, Math.ceil((deadline - Date.now()) / (24 * 60 * 60 * 1000)));
}

/** İnsanın okuyabileceği boyut — Desk'in `lib/format.ts`'iyle aynı biçim. */
export function formatSize(bytes: number | null): string {
  if (bytes == null) return '—';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex++;
  }
  return `${value.toFixed(value < 10 ? 1 : 0)} ${units[unitIndex]}`;
}
