import api from './api';

/**
 * Desk'in GERÇEK veri yolu: oturum (cookie) + projectId ile `/api/gateway/*`.
 * Bağlantı dizesi hiç istemciden geçmiyor; sunucu projenin saklı bağlantısını
 * erişim kontrolünden sonra kendisi çözüyor.
 */

export type DeskRow = Record<string, unknown>;

export interface DeskListOptions {
  page: number;
  pageSize: number;
  orderByColumn?: string | null;
  sortDirection?: 'asc' | 'desc';
}

export interface DeskListResult {
  rows: DeskRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  /** false: sıralama kolonu yok, sayfalar arasında tekrar/atlama olabilir. */
  stablePagination: boolean;
}

export interface DeskSqlResult {
  columns: string[];
  rows: DeskRow[];
  affectedRows: number;
  truncated: boolean;
  /** İstemcinin ölçtüğü uçtan uca süre; sunucu süre döndürmüyor. */
  durationMs: number;
}

/**
 * - `sql-disabled`: Owner bu proje için SQL konsolunu açmamış (403).
 * - `forbidden`: başka bir 403 (ör. Owner değil, projeye erişim yok).
 */
export type DeskErrorKind = 'sql-disabled' | 'forbidden' | 'unauthorized' | 'feature-disabled' | 'other';

export class DeskError extends Error {
  constructor(message: string, readonly status: number | null, readonly kind: DeskErrorKind) {
    super(message);
    this.name = 'DeskError';
  }
}

interface WireRow { values?: DeskRow }

function toDeskError(err: unknown): DeskError {
  if (err instanceof DeskError) return err;
  const e = err as {
    response?: { status?: number; data?: { message?: string; error?: string; code?: string } };
    message?: string;
  };
  const status = e?.response?.status ?? null;
  const data = e?.response?.data;
  const message = data?.message || data?.error || e?.message || 'Request failed.';

  let kind: DeskErrorKind = 'other';
  if (status === 403) kind = /not enabled/i.test(message) ? 'sql-disabled' : 'forbidden';
  else if (status === 401) kind = 'unauthorized';
  else if (status === 503 && data?.code === 'FEATURE_DISABLED') kind = 'feature-disabled';
  return new DeskError(message, status, kind);
}

async function post<T>(path: string, body: unknown): Promise<T> {
  try {
    const res = await api.post<T>(path, body);
    return res.data;
  } catch (err) {
    throw toDeskError(err);
  }
}

const unwrap = (rows: WireRow[] | undefined): DeskRow[] => (rows ?? []).map((r) => r.values ?? {});

/** Boş bağlantı/dbType: sunucu bunları oturum + projectId'den çözüyor. */
const base = (projectId: string, tableName: string) => ({
  connectionString: '',
  dbType: '',
  tableName,
  projectId,
});

export async function listRows(
  projectId: string, tableName: string, opts: DeskListOptions,
): Promise<DeskListResult> {
  const data = await post<{
    rows: WireRow[]; page: number; pageSize: number; totalCount: number; stablePagination?: boolean;
  }>('/gateway/list', {
    ...base(projectId, tableName),
    page: opts.page,
    pageSize: opts.pageSize,
    includeTotalCount: true,
    orderByColumn: opts.orderByColumn ?? null,
    sortDirection: opts.sortDirection === 'desc' ? 'Desc' : 'Asc',
  });
  return {
    rows: unwrap(data.rows),
    page: data.page,
    pageSize: data.pageSize,
    totalCount: data.totalCount,
    stablePagination: data.stablePagination !== false,
  };
}

/** Sonuç kolonları ilk satırın anahtarlarından türetilir; 0 satırda kolon bilgisi yoktur. */
export function columnsOf(rows: DeskRow[]): string[] {
  const seen: string[] = [];
  for (const row of rows.slice(0, 50)) {
    for (const key of Object.keys(row)) if (!seen.includes(key)) seen.push(key);
  }
  return seen;
}

export async function runSql(projectId: string, sql: string, maxRows = 500): Promise<DeskSqlResult> {
  const started = performance.now();
  const data = await post<{ rows: WireRow[]; affectedRows: number; truncated: boolean }>(
    '/gateway/desk-sql', { projectId, sql, maxRows });
  const rows = unwrap(data.rows);
  return {
    columns: columnsOf(rows),
    rows,
    affectedRows: data.affectedRows,
    truncated: Boolean(data.truncated),
    durationMs: Math.max(1, Math.round(performance.now() - started)),
  };
}

export async function createRow(
  projectId: string, tableName: string, values: Record<string, string | null>,
): Promise<void> {
  await post('/gateway/create', { ...base(projectId, tableName), values });
}

export async function updateRow(
  projectId: string, tableName: string, pkColumn: string, pkValue: string,
  values: Record<string, string | null>,
): Promise<void> {
  await post('/gateway/update', { ...base(projectId, tableName), pkColumn, pkValue, values });
}

export async function deleteRow(
  projectId: string, tableName: string, pkColumn: string, pkValue: string,
): Promise<void> {
  await post('/gateway/delete', { ...base(projectId, tableName), pkColumn, pkValue });
}

export async function bulkDeleteRows(
  projectId: string, tableName: string, pkColumn: string, pkValues: string[],
): Promise<void> {
  await post('/gateway/bulk-delete', { ...base(projectId, tableName), pkColumn, pkValues });
}
