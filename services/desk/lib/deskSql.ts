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

// ── Sorgu geçmişi (F-01) ve kaydedilmiş sorgular (F-02) ─────────────────────
//
// Aynı dosyada, `deskApi`'de değil: bunlar SQL konsolunun yan defterleri ve
// sunucuda da aynı kapıdan geçiyorlar (Owner + `AllowDeskSql`). Genel veri
// yoluna koymak, konsolu kapatmanın bu uçları kapatmadığı izlenimi verirdi.

export interface SqlHistoryItem {
  id: string;
  sql: string;
  truncated: boolean;
  rowCount: number;
  succeeded: boolean;
  errorMessage: string | null;
  durationMs: number;
  createdAt: string;
}

export interface SavedQueryItem {
  id: string;
  name: string;
  sql: string;
  createdAt: string;
  updatedAt: string;
}

async function call<T>(session: DeskSession, path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API}/api/gateway/${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${session.token}`,
      ...(init?.headers ?? {}),
    },
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

  return res.status === 204 ? (undefined as T) : (res.json() as Promise<T>);
}

const projectQuery = (session: DeskSession) =>
  `projectId=${encodeURIComponent(session.projectId)}`;

export async function getSqlHistory(session: DeskSession, limit = 50): Promise<SqlHistoryItem[]> {
  const body = await call<{ items: SqlHistoryItem[] }>(
    session, `desk-sql/history?${projectQuery(session)}&limit=${limit}`);
  return body.items;
}

export function clearSqlHistory(session: DeskSession): Promise<{ deleted: number }> {
  return call(session, `desk-sql/history?${projectQuery(session)}`, { method: 'DELETE' });
}

export async function getSavedQueries(session: DeskSession): Promise<SavedQueryItem[]> {
  const body = await call<{ items: SavedQueryItem[] }>(
    session, `desk-sql/saved?${projectQuery(session)}`);
  return body.items;
}

export function saveQuery(session: DeskSession, name: string, sql: string): Promise<SavedQueryItem> {
  return call(session, 'desk-sql/saved', {
    method: 'POST',
    body: JSON.stringify({ projectId: session.projectId, name, sql }),
  });
}

export function deleteSavedQuery(session: DeskSession, id: string): Promise<void> {
  return call(session, `desk-sql/saved/${encodeURIComponent(id)}?${projectQuery(session)}`,
    { method: 'DELETE' });
}
