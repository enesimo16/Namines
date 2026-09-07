import type { DeskTable } from './schema';

/**
 * Namines Desk'in ana backend'e TEK bağlantı noktası.
 *
 * <b>Mikroservis sınırı burasıdır.</b> Desk, Namines'in koduna değil yalnızca
 * HTTP sözleşmesine bağlı. Ana backend'in iç tipleri buraya sızmıyor —
 * `DeskTable` gibi tipler bu serviste ayrıca tanımlı (bilinçli kopya).
 *
 * Kimlik: oturum (JWT, `Authorization: Bearer`) + `projectId`. Bağlantı dizesi
 * ve parola ASLA istemciye gelmez, sunucu `projectId` + oturum sahibinin bu
 * projeye erişimini doğruladıktan sonra şifreli bağlantıyı çözer
 * (01-KIMLIK-VE-OTURUM.md §3). v0.1'deki ham API anahtarının yerini bu aldı.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export class DeskApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

/** Bir oturumu ve seçili projeyi taşıyan çağrı bağlamı. */
export interface DeskSession { token: string; projectId: string; }

/** Namines.Core.Models.GatewayOperator ile birebir — enum adı STRING olarak gider (JsonStringEnumConverter). */
export type GatewayOperator = 'Eq' | 'Neq' | 'Gt' | 'Gte' | 'Lt' | 'Lte' | 'Like' | 'In' | 'IsNull' | 'IsNotNull';
export type GatewaySortDirection = 'Asc' | 'Desc';
export interface GatewayFilter { column: string; operator: GatewayOperator; values: (string | null)[]; }

async function call(path: string, session: DeskSession, init?: RequestInit): Promise<Response> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${session.token}`,
      ...(init?.headers ?? {}),
    },
    cache: 'no-store',
  });

  if (!res.ok) {
    // Sunucunun kendi mesajını taşı — "bir hata oluştu" demek, kullanıcıyı
    // 403 (izin yok) ile 500 (bağlantı koptu) arasında kör bırakırdı.
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.message) message = body.message;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new DeskApiError(message, res.status);
  }

  return res;
}

async function callJson<T>(path: string, session: DeskSession, init?: RequestInit): Promise<T> {
  const res = await call(path, session, init);
  return res.json() as Promise<T>;
}

export interface DeskRow { values: Record<string, unknown>; }
export interface ListResult { rows: DeskRow[]; page: number; pageSize: number; totalCount: number | null; }

/** D4 §2.2: sıralama + filtre — hepsi Gateway'in zaten desteklediği alanlar (backend değişikliği gerekmiyor). */
export interface ListOptions {
  orderByColumn?: string | null;
  sortDirection?: GatewaySortDirection;
  filters?: GatewayFilter[];
  select?: string[];
}

export const deskApi = {
  /** İzinli tabloların kolon meta verisi — formlar buradan DETERMİNİSTİK üretilir. */
  schema: (session: DeskSession) =>
    callJson<{ tables: DeskTable[] }>(`/api/gateway/schema?projectId=${encodeURIComponent(session.projectId)}`, session),

  list: (session: DeskSession, table: string, page: number, pageSize: number, options?: ListOptions) =>
    callJson<ListResult>('/api/gateway/list', session, {
      method: 'POST',
      body: JSON.stringify({
        // Boş: sunucu bağlantıyı oturum + projectId'den çözecek.
        connectionString: '', dbType: '', tableName: table, projectId: session.projectId,
        page, pageSize, includeTotalCount: true,
        orderByColumn: options?.orderByColumn ?? null,
        sortDirection: options?.sortDirection ?? 'Asc',
        filters: options?.filters ?? null,
        select: options?.select ?? null,
      }),
    }),

  create: (session: DeskSession, table: string, values: Record<string, string | null>) =>
    callJson<{ affectedRows: number; row: DeskRow | null }>('/api/gateway/create', session, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, projectId: session.projectId, values }),
    }),

  update: (session: DeskSession, table: string, pkColumn: string, pkValue: string, values: Record<string, string | null>) =>
    callJson<{ affectedRows: number }>('/api/gateway/update', session, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, projectId: session.projectId, pkColumn, pkValue, values }),
    }),

  remove: (session: DeskSession, table: string, pkColumn: string, pkValue: string) =>
    callJson<{ affectedRows: number }>('/api/gateway/delete', session, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, projectId: session.projectId, pkColumn, pkValue }),
    }),

  /** Namines Desk v2 §E4.1 — toplu silme, TEK işlemde (ya hepsi ya hiçbiri). */
  bulkRemove: (session: DeskSession, table: string, pkColumn: string, pkValues: string[]) =>
    callJson<{ affectedRows: number }>('/api/gateway/bulk-delete', session, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, projectId: session.projectId, pkColumn, pkValues }),
    }),

  /**
   * D4 §2.3 — dışa aktarma. `POST /api/gateway/export` bir dosya (CSV/JSON)
   * döndürür, JSON gövde değil; bu yüzden `callJson` değil ham `call` kullanılıyor.
   */
  async export(
    session: DeskSession, table: string, format: 'csv' | 'json',
    options?: Pick<ListOptions, 'orderByColumn' | 'sortDirection' | 'filters'>,
  ): Promise<{ blob: Blob; fileName: string }> {
    const res = await call('/api/gateway/export', session, {
      method: 'POST',
      body: JSON.stringify({
        connectionString: '', dbType: '', tableName: table, projectId: session.projectId,
        format, maxRows: 10_000,
        orderByColumn: options?.orderByColumn ?? null,
        sortDirection: options?.sortDirection ?? 'Asc',
        filters: options?.filters ?? null,
      }),
    });
    const disposition = res.headers.get('content-disposition') ?? '';
    const match = /filename="?([^"]+)"?/.exec(disposition);
    return { blob: await res.blob(), fileName: match?.[1] ?? `${table}.${format}` };
  },
};
