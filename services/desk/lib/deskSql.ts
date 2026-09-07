import type { DeskSession } from './api';

/**
 * Namines Desk v2 §E4.2 — salt-okunur SQL konsolu.
 *
 * Bilinçli olarak `deskApi`'nin (lib/api.ts) parçası DEĞİL: o dosya Desk'in
 * TÜM oturum sahiplerinin kullandığı genel veri yolu, bu ise yalnızca proje
 * Owner'ına açık, ayrıca opt-in gerektiren ayrı bir yüzey — karıştırılmaması
 * için ayrı dosyada.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export class DeskSqlError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export interface DeskSqlRow { values: Record<string, unknown>; }
export interface DeskSqlResult { rows: DeskSqlRow[]; affectedRows: number; truncated: boolean; }

export async function runDeskSql(
  session: DeskSession, sql: string, maxRows = 500,
): Promise<DeskSqlResult> {
  const res = await fetch(`${API}/api/gateway/desk-sql`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${session.token}` },
    body: JSON.stringify({ projectId: session.projectId, sql, maxRows }),
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.message) message = body.message;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new DeskSqlError(message, res.status);
  }

  return res.json() as Promise<DeskSqlResult>;
}

/** Owner'ın SQL konsolunu bu proje için açıp kapatması. */
export async function setDeskSqlEnabled(session: DeskSession, enabled: boolean): Promise<void> {
  const res = await fetch(`${API}/api/gateway/keys/project/${encodeURIComponent(session.projectId)}/desk-sql`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${session.token}` },
    body: JSON.stringify({ enabled }),
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = 'Ayar kaydedilemedi.';
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new DeskSqlError(message, res.status);
  }
}
