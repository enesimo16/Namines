import { describe, it, expect, vi, beforeEach } from 'vitest';

const fakeApi = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  patch: vi.fn(),
  delete: vi.fn(),
  interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } },
}));

vi.mock('axios', () => ({ default: { create: () => fakeApi } }));

import { hubService } from '../services/hubService';
import { changeRequestService } from '../services/api';
import type { DatabaseSchema } from '../types/schema';

/**
 * Sunucu hatasında "sahte başarı" üretmeme sözleşmesi.
 *
 * Eskiden her hata yutulup localStorage'a bir issue / değişiklik talebi / test sonucu
 * yazılıyor ve kullanıcıya başarılı gibi gösteriliyordu. Yerel taklit artık yalnızca
 * açıkça yerel olan taslaklarda (hub: repo.isLocalDraft, CR: 'local-project' / 'local-cr-*').
 */

const networkError = Object.assign(new Error('Network Error'), { response: undefined });
const schema: DatabaseSchema = { schemaId: 's', name: 'S', tables: [], relations: [] };

beforeEach(() => {
  vi.clearAllMocks();
  fakeApi.get.mockRejectedValue(networkError);
  fakeApi.post.mockRejectedValue(networkError);
  fakeApi.patch.mockRejectedValue(networkError);
  fakeApi.put.mockRejectedValue(networkError);
});

describe('hubService yerel taklit', () => {
  it('gerçek repoda issue oluşturma hatası çağırana fırlatılır', async () => {
    await expect(hubService.createIssue('repo-1', { title: 'x' })).rejects.toThrow('Network Error');
  });

  it('gerçek repoda issue listeleme hatası fırlatılır', async () => {
    await expect(hubService.listIssues('repo-1')).rejects.toThrow('Network Error');
  });

  it('gerçek repoda issue durumu değişimi hatası fırlatılır', async () => {
    await expect(hubService.updateIssueState('i1', true)).rejects.toThrow('Network Error');
  });

  it('yerel taslak repoda issue yerelde oluşturulur', async () => {
    const issue = await hubService.createIssue('draft-1', { title: 'x' }, { localDraft: true });
    expect(issue.title).toBe('x');
  });
});

describe('changeRequestService yerel taklit', () => {
  it('gerçek projede createQuick hatası fırlatılır', async () => {
    await expect(changeRequestService.createQuick('project-1', schema)).rejects.toThrow('Network Error');
  });

  it("'local-project' sentinel'i yerel talep üretir ve doğrulama uydurmaz", async () => {
    const { id } = await changeRequestService.createQuick('local-project', schema);
    expect(id.startsWith('local-cr-')).toBe(true);
  });

  it('gerçek projede liste hatası fırlatılır (başka projelerin yerel talepleri karışmaz)', async () => {
    await expect(changeRequestService.listForProject('project-1')).rejects.toThrow('Network Error');
  });

  it('sunucu kimlikli talepte runTests başarı uydurmaz', async () => {
    await expect(changeRequestService.runTests('server-cr-1')).rejects.toThrow('Network Error');
  });

  it('yerel taslakta runTests "desteklenmiyor" döner, başarı uydurmaz', async () => {
    const result = await changeRequestService.runTests('local-cr-1');
    expect(result.supported).toBe(false);
    expect(result.success).toBe(false);
  });

  it('otomatik onay ayarı yazılamazsa başarı döndürülmez', async () => {
    await expect(changeRequestService.setAutoApproveSafe('project-1', true)).rejects.toThrow('Network Error');
  });
});
