import type { DeskSession } from './api';

/**
 * Namines Ground — yönetilen veritabanı istemcisi.
 *
 * <b>Ground'un kendi sitesi yok</b>; Vault'la aynı gerekçe: kullanıcıya bakan
 * yüzü Desk'in içindeki görünüm. Ayrıntı: `namines-ground/02-V1-KARARLARI.md`.
 *
 * Bağlantı dizesi buraya HİÇ gelmez — sunucuda şifreli durur, Desk yalnızca
 * "bir veritabanı var mı, durumu ne" bilgisini görür.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export type GroundStatus = 'Provisioning' | 'Active' | 'PendingDelete' | 'Deleted' | 'Failed';

export interface GroundProvider {
  name: string;
  /**
   * Bu sağlayıcı gerçek bir kaynağa karşı canlı denendi mi.
   *
   * Arayüzde gösteriliyor: denenmemiş bir sağlayıcıyı denenmiş gibi sunmak,
   * kullanıcının verisini kanıtlanmamış bir yola koymasına sessizce izin
   * vermek olurdu.
   */
  liveVerified: boolean;
  supportsBranching: boolean;
  supportsRegionChoice: boolean;
  /** "Bu kaynağı kim işletiyor" — sorumluluk notu. */
  responsibility: string;
  /** Yapılandırma eksikse ya da erişilemiyorsa açıklaması; yoksa null. */
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
  /** Silme isteginden kalici silmeye kac gun -- sunucudan geliyor ki arayuz
      kendi sabitini tutmasin ve ikisi ayrismasin. */
  graceDays: number;
}

export class GroundError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

async function call(path: string, session: DeskSession, init?: RequestInit): Promise<Response> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${session.token}`, ...(init?.headers ?? {}) },
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new GroundError(message, res.status);
  }

  return res;
}

export const groundApi = {
  providers: async (session: DeskSession): Promise<GroundProvider[]> =>
    (await call('/api/ground/providers', session)).json(),

  /** Projenin kaydı. Yoksa `null` — sunucu 204 döner. */
  get: async (session: DeskSession): Promise<GroundDatabase | null> => {
    const res = await call(`/api/ground/${encodeURIComponent(session.projectId)}`, session);
    return res.status === 204 ? null : res.json();
  },

  provision: async (session: DeskSession, provider: string): Promise<GroundDatabase> =>
    (await call(`/api/ground/${encodeURIComponent(session.projectId)}/provision`, session, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ provider }),
    })).json(),

  /**
   * Silmeyi ister. Kaynak HEMEN silinmez — bekleme penceresi başlar.
   * `confirmProjectName` kullanıcının eliyle yazdığı ad.
   */
  requestDelete: async (session: DeskSession, confirmProjectName: string): Promise<GroundDatabase> =>
    (await call(`/api/ground/${encodeURIComponent(session.projectId)}/delete`, session, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmProjectName }),
    })).json(),

  cancelDelete: async (session: DeskSession): Promise<GroundDatabase> =>
    (await call(`/api/ground/${encodeURIComponent(session.projectId)}/cancel-delete`, session, {
      method: 'POST',
    })).json(),
};

/**
 * Silmeye kaç gün kaldığı. Bekleme penceresi dolmuşsa 0.
 *
 * <b>Gün cinsinden ve YUKARI yuvarlanıyor:</b> "1 gün kaldı" demek, 23 saat
 * kalmışken "0 gün" demekten daha doğru bir uyarıdır — kullanıcı hâlâ
 * geri alabilir.
 */
export function daysUntilPurge(deleteRequestedAt: string, graceDays: number): number {
  const deadline = new Date(deleteRequestedAt).getTime() + graceDays * 24 * 60 * 60 * 1000;
  const remaining = deadline - Date.now();

  if (!Number.isFinite(remaining) || remaining <= 0) return 0;
  return Math.ceil(remaining / (24 * 60 * 60 * 1000));
}
