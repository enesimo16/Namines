import type { DeskSession } from './api';

/**
 * Namines Desk v2 §E1.1 — API anahtarı yönetim ekranı. Backend **zaten
 * tamamen hazır** (GatewayKeyController: List/Create/Revoke, Admin ve üstü
 * yetkili) — bu tamamen bir arayüz işi, yeni bir uç gerekmedi.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export class ApiKeysError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export interface GatewayApiKeySummary {
  id: string; name: string; prefix: string; canWrite: boolean; canExecuteSql: boolean;
  allowedOrigins: string | null; allowedIps: string | null; rateLimitPerMinute: number;
  createdAt: string; expiresAt: string | null; revokedAt: string | null; lastUsedAt: string | null;
}

async function req<T>(path: string, session: DeskSession, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${session.token}`, ...(init?.headers ?? {}) },
    cache: 'no-store',
  });
  if (!res.ok) {
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new ApiKeysError(message, res.status);
  }
  return res.json() as Promise<T>;
}

export const apiKeysApi = {
  list: (session: DeskSession) =>
    req<GatewayApiKeySummary[]>(`/api/gateway/keys/${encodeURIComponent(session.projectId)}`, session),

  create: (session: DeskSession, name: string, canWrite: boolean) =>
    req<GatewayApiKeySummary & { key: string; warning: string }>(
      `/api/gateway/keys/${encodeURIComponent(session.projectId)}`, session,
      { method: 'POST', body: JSON.stringify({ name, canWrite }) },
    ),

  /** Sunucu 204 (No Content) döner — JSON gövde ayrıştırılmıyor. */
  async revoke(session: DeskSession, keyId: string): Promise<void> {
    const res = await fetch(
      `${API}/api/gateway/keys/${encodeURIComponent(session.projectId)}/${encodeURIComponent(keyId)}`,
      { method: 'DELETE', headers: { Authorization: `Bearer ${session.token}` }, cache: 'no-store' },
    );
    if (!res.ok) {
      let message = `İstek başarısız (${res.status}).`;
      try {
        const body = await res.json();
        if (body?.error) message = body.error;
      } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
      throw new ApiKeysError(message, res.status);
    }
  },
};
