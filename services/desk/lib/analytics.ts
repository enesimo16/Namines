import type { DeskSession } from './api';

/**
 * D7 — Analytics (namines_desk/07-ANALYTICS.md). ⚠️ Vercel'in Edge Requests/
 * CPU gibi metriklerinin karşılığı YOK — Namines kimsenin uygulamasını
 * çalıştırmıyor. Tek gerçek kaynak `GatewayAuditEntry` (yalnızca yazma).
 *
 * `GET /api/gateway/analytics/{projectId}` Admin ve üstü yetki ister (Logs
 * ile aynı) — toplamlar da projenin veri hareketinin tamamını açığa vurur.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export class AnalyticsAccessError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export interface AnalyticsBucket {
  bucketStart: string;
  create: number; update: number; delete: number; import: number; rpc: number; sql: number;
}

export interface TopTable { tableName: string; count: number; }

export interface AnalyticsResult {
  from: string; to: string; bucket: 'hour' | 'day';
  buckets: AnalyticsBucket[];
  totalWrites: number;
  successRate: number;
  totalAffectedRows: number;
  topTables: TopTable[];
  sourceBreakdown: { human: number; application: number };
  schemaVersionCount: number;
}

export async function fetchAnalytics(
  session: DeskSession, projectId: string, from: string, to: string, bucket: 'hour' | 'day',
): Promise<AnalyticsResult> {
  const params = new URLSearchParams({ from, to, bucket });
  const res = await fetch(`${API}/api/gateway/analytics/${encodeURIComponent(projectId)}?${params}`, {
    headers: { Authorization: `Bearer ${session.token}` },
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new AnalyticsAccessError(message, res.status);
  }

  return res.json() as Promise<AnalyticsResult>;
}
