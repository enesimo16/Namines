import type { DeskSession } from './api';

/**
 * D6 — Logs (namines_desk/06-LOGS.md). ⚠️ Bu "tüm istekler" logu DEĞİL —
 * `GatewayAuditEntry` yalnızca YAZMA işlemlerini kaydediyor (Create/Update/
 * Delete/Import/Rpc/Sql); okuma (`list`/`detail`/`schema`) hiç kaydedilmiyor.
 * Bu bilinçli bir tasarım (§1) — ekranın adı ve boş durumu bunu açıkça söyler.
 *
 * `GET /api/gateway/keys/{projectId}/audit` Admin ve üstü yetki istiyor (07 §5)
 * — Desk bu kuralı gevşetmiyor; yetkisiz kullanıcı açıklayıcı bir 403 alır.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

/**
 * `read` sonradan eklendi: denetim kaydı artık MASKELİ kolon içeren tablolardan
 * yapılan okumaları da tutuyor (bkz. GatewayController.MaskAsync). Maskeleme
 * konulmuş bir kolon, sahibinin "bu veri hassas" dediği kolondur — ona kimin
 * eriştiği tam da kaydedilmesi gereken şey. Her okuma değil, yalnızca bunlar:
 * bir liste ekranının saniyede onlarca okumasını kaydetmek kaydı kullanılamaz
 * hâle getirirdi.
 */
export type GatewayWriteKind = 'create' | 'update' | 'delete' | 'import' | 'rpc' | 'sql' | 'read';

export interface AuditLogEntry {
  id: string;
  kind: GatewayWriteKind;
  tableName: string | null;
  rowKey: string | null;
  columns: string | null;
  affectedRows: number;
  succeeded: boolean;
  apiKeyPrefix: string | null;
  actorUserId: string | null;
  createdAt: string;
}

export interface AuditLogPage { entries: AuditLogEntry[]; totalCount: number; page: number; pageSize: number; }

export class LogsAccessError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export interface AuditLogFilters {
  from?: string; to?: string;
  kinds?: GatewayWriteKind[];
  tableName?: string;
  succeeded?: boolean;
}

export async function fetchAuditLog(
  session: DeskSession, projectId: string, page: number, pageSize: number, filters?: AuditLogFilters,
): Promise<AuditLogPage> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (filters?.from) params.set('from', filters.from);
  if (filters?.to) params.set('to', filters.to);
  if (filters?.tableName) params.set('tableName', filters.tableName);
  if (filters?.succeeded !== undefined) params.set('succeeded', String(filters.succeeded));
  // ASP.NET'in enum model binder'ı isim eşleşmesi bekliyor — sunucudaki
  // `GatewayWriteKind` üyeleriyle BİREBİR aynı büyük harfle yazılmalı
  // (yanıttaki küçük-harfli `kind` alanı yalnızca GÖSTERİM için; bu ayrı).
  const kindToServerName: Record<GatewayWriteKind, string> = {
    create: 'Create', update: 'Update', delete: 'Delete', import: 'Import', rpc: 'Rpc', sql: 'Sql',
    read: 'Read',
  };
  for (const k of filters?.kinds ?? []) params.append('kinds', kindToServerName[k]);

  const res = await fetch(`${API}/api/gateway/keys/${encodeURIComponent(projectId)}/audit?${params}`, {
    headers: { Authorization: `Bearer ${session.token}` },
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new LogsAccessError(message, res.status);
  }

  return res.json() as Promise<AuditLogPage>;
}
