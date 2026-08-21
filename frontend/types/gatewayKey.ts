/**
 * Gateway API anahtarları ve tablo izinleri (new-phase/08-GATEWAY-API.md §4.3).
 */

export interface GatewayKey {
  id: string;
  name: string;
  /** Anahtarın gösterilebilir baş kısmı. Gizli değildir, tek başına kimlik doğrulamaz. */
  prefix: string;
  canWrite: boolean;
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  lastUsedAt: string | null;
}

/**
 * Anahtar üretme yanıtı. `key` alanı SADECE burada gelir — sunucuda yalnızca
 * özeti saklanıyor, bu yanıt kaybolursa anahtar geri getirilemez.
 */
export interface GatewayKeyCreated extends Omit<GatewayKey, 'createdAt' | 'revokedAt' | 'lastUsedAt'> {
  key: string;
  warning: string;
}

export interface GatewayTablePermission {
  tableName: string;
  canRead: boolean;
  canWrite: boolean;
  updatedAt?: string;
}

/** İzin satırı olmayan tablo erişilemez demektir (08 §1). */
export const NO_ACCESS: GatewayTablePermission = {
  tableName: '',
  canRead: false,
  canWrite: false,
};
