import type { DeskTable } from './schema';

/**
 * Namines Desk'in ana backend'e TEK bağlantı noktası.
 *
 * <b>Mikroservis sınırı burasıdır.</b> Desk, Namines'in koduna değil yalnızca
 * HTTP sözleşmesine bağlı. Ana backend'in iç tipleri buraya sızmıyor —
 * `DeskTable` gibi tipler bu serviste ayrıca tanımlı (bilinçli kopya).
 *
 * Kimlik: yalnızca API anahtarı (`X-Namines-Key`). Bağlantı dizesi ve parola
 * ASLA istemciye gelmez, sunucu anahtardan çözer.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export class DeskApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

async function call<T>(path: string, key: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Namines-Key': key,
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

  return res.json() as Promise<T>;
}

export interface DeskRow { values: Record<string, unknown>; }
export interface ListResult { rows: DeskRow[]; page: number; pageSize: number; totalCount: number | null; }

export const deskApi = {
  /** İzinli tabloların kolon meta verisi — formlar buradan DETERMİNİSTİK üretilir. */
  schema: (key: string) =>
    call<{ tables: DeskTable[] }>('/api/gateway/schema', key),

  list: (key: string, table: string, page: number, pageSize: number) =>
    call<ListResult>('/api/gateway/list', key, {
      method: 'POST',
      body: JSON.stringify({
        // Boş: sunucu bağlantıyı anahtardan çözecek.
        connectionString: '', dbType: '', tableName: table,
        page, pageSize, includeTotalCount: true,
      }),
    }),

  create: (key: string, table: string, values: Record<string, string | null>) =>
    call<{ affectedRows: number; row: DeskRow | null }>('/api/gateway/create', key, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, values }),
    }),

  update: (key: string, table: string, pkColumn: string, pkValue: string, values: Record<string, string | null>) =>
    call<{ affectedRows: number }>('/api/gateway/update', key, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, pkColumn, pkValue, values }),
    }),

  remove: (key: string, table: string, pkColumn: string, pkValue: string) =>
    call<{ affectedRows: number }>('/api/gateway/delete', key, {
      method: 'POST',
      body: JSON.stringify({ connectionString: '', dbType: '', tableName: table, pkColumn, pkValue }),
    }),
};
