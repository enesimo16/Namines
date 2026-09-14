import api from './api';

/**
 * "/compile"'da onaylanmış bir şemayı çalışan bir Desk paneline (ve isteğe
 * bağlı indirilebilir bir kod projesine) dönüştüren tek uç için istemci.
 * Sağlayıcı listesi için AYRI bir çağrı YOK — Ground'un zaten var olan
 * `groundApi.providers()` (services/vaultGroundApi.ts) burada da kullanılıyor.
 */

export class LaunchError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export type LaunchOutcome =
  | { status: 'Ready'; projectId: string; deskHandoffToken: string; backupWarning: string | null }
  | { status: 'NeedsReview' }
  | { status: 'ProvisionFailed' | 'DdlFailed'; error: string };

function toLaunchError(err: unknown, fallback: string): never {
  const status = (err as { response?: { status?: number; data?: { error?: string } } })?.response?.status ?? 0;
  const body = (err as { response?: { data?: { error?: string } } })?.response?.data;
  throw new LaunchError(body?.error ?? fallback, status);
}

export const launchApi = {
  launch: async (projectId: string, provider: string, ddlScript: string): Promise<LaunchOutcome> => {
    try {
      const res = await api.post('/launch', { projectId, provider, ddlScript });
      return res.data;
    } catch (err) {
      // 400 gövdesi zaten { status, error } şeklinde geliyor — bunu olduğu gibi taşı.
      const body = (err as { response?: { data?: LaunchOutcome } })?.response?.data;
      if (body && 'status' in body) return body;
      toLaunchError(err, 'Launch failed.');
    }
  },

  download: async (projectId: string): Promise<{ blob: Blob; fileName: string }> => {
    try {
      const res = await api.post(`/launch/${encodeURIComponent(projectId)}/download`, null, {
        responseType: 'blob',
      });
      const disposition = res.headers['content-disposition'] ?? '';
      const match = /filename="?([^"]+)"?/.exec(disposition);
      return { blob: res.data, fileName: match?.[1] ?? 'namines-project.zip' };
    } catch (err) {
      toLaunchError(err, 'Download failed.');
    }
  },
};
