// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';

const post = vi.hoisted(() => vi.fn());
vi.mock('../../services/api', () => ({ default: { post } }));

import {
  listRows, runSql, updateRow, createRow, deleteRow, bulkDeleteRows, columnsOf, DeskError,
} from '../../services/deskService';

const axiosError = (status: number, data: Record<string, unknown>) =>
  Object.assign(new Error('Request failed'), { response: { status, data } });

describe('deskService', () => {
  beforeEach(() => vi.clearAllMocks());

  it('list: oturum + projectId ile çağırır, bağlantı dizesi göndermez, satırları açar', async () => {
    post.mockResolvedValue({
      data: { rows: [{ values: { id: 1 } }, { values: { id: 2 } }], page: 2, pageSize: 10, totalCount: 42, stablePagination: false },
    });

    const res = await listRows('p1', 'users', { page: 2, pageSize: 10, orderByColumn: 'id', sortDirection: 'desc' });

    expect(post).toHaveBeenCalledWith('/gateway/list', expect.objectContaining({
      connectionString: '', dbType: '', tableName: 'users', projectId: 'p1',
      page: 2, pageSize: 10, includeTotalCount: true, orderByColumn: 'id', sortDirection: 'Desc',
    }));
    expect(res).toEqual({ rows: [{ id: 1 }, { id: 2 }], page: 2, pageSize: 10, totalCount: 42, stablePagination: false });
  });

  it('list: sıralama verilmezse Asc ve null kolon gönderir', async () => {
    post.mockResolvedValue({ data: { rows: [], page: 1, pageSize: 25, totalCount: 0 } });
    const res = await listRows('p1', 't', { page: 1, pageSize: 25 });
    expect(post.mock.calls[0][1]).toMatchObject({ orderByColumn: null, sortDirection: 'Asc' });
    expect(res.stablePagination).toBe(true);
  });

  it('desk-sql: kolonları satırlardan türetir, kesilmeyi ve süreyi döndürür', async () => {
    post.mockResolvedValue({
      data: { rows: [{ values: { a: 1, b: 'x' } }, { values: { a: 2, c: null } }], affectedRows: -1, truncated: true },
    });

    const res = await runSql('p1', 'SELECT 1', 100);

    expect(post).toHaveBeenCalledWith('/gateway/desk-sql', { projectId: 'p1', sql: 'SELECT 1', maxRows: 100 });
    expect(res.columns).toEqual(['a', 'b', 'c']);
    expect(res.truncated).toBe(true);
    expect(res.durationMs).toBeGreaterThanOrEqual(1);
  });

  it('desk-sql 403 "not enabled" → sql-disabled, başka 403 → forbidden', async () => {
    post.mockRejectedValueOnce(axiosError(403, { message: 'The SQL console is not enabled for this project yet.' }));
    await expect(runSql('p1', 'SELECT 1')).rejects.toMatchObject({ kind: 'sql-disabled', status: 403 });

    post.mockRejectedValueOnce(axiosError(403, { message: 'Only the project Owner can use the Desk SQL console.' }));
    const err = await runSql('p1', 'SELECT 1').catch((e) => e);
    expect(err).toBeInstanceOf(DeskError);
    expect(err.kind).toBe('forbidden');
    expect(err.message).toBe('Only the project Owner can use the Desk SQL console.');
  });

  it('sunucu mesajını korur; 503 FEATURE_DISABLED ve 401 ayrı türde', async () => {
    post.mockRejectedValueOnce(axiosError(400, { message: 'Only a single SELECT statement is allowed.' }));
    await expect(runSql('p1', 'DROP TABLE x')).rejects.toMatchObject({
      kind: 'other', message: 'Only a single SELECT statement is allowed.',
    });

    post.mockRejectedValueOnce(axiosError(503, { code: 'FEATURE_DISABLED', error: 'Disabled by admin.' }));
    await expect(runSql('p1', 'SELECT 1')).rejects.toMatchObject({ kind: 'feature-disabled', message: 'Disabled by admin.' });

    post.mockRejectedValueOnce(axiosError(401, {}));
    await expect(listRows('p1', 't', { page: 1, pageSize: 1 })).rejects.toMatchObject({ kind: 'unauthorized' });
  });

  it('yazma uçları PK ve değerleri projectId ile gönderir', async () => {
    post.mockResolvedValue({ data: {} });
    await createRow('p1', 'users', { name: 'Ada' });
    await updateRow('p1', 'users', 'id', '7', { name: 'Grace' });
    await deleteRow('p1', 'users', 'id', '7');
    await bulkDeleteRows('p1', 'users', 'id', ['1', '2']);

    expect(post.mock.calls.map((c) => c[0])).toEqual([
      '/gateway/create', '/gateway/update', '/gateway/delete', '/gateway/bulk-delete',
    ]);
    expect(post.mock.calls[1][1]).toMatchObject({ projectId: 'p1', pkColumn: 'id', pkValue: '7', values: { name: 'Grace' } });
    expect(post.mock.calls[3][1]).toMatchObject({ pkValues: ['1', '2'] });
  });

  it('columnsOf boş sonuçta boş döner', () => {
    expect(columnsOf([])).toEqual([]);
  });
});
