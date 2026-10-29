// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';

const get = vi.fn();
const put = vi.fn();
vi.mock('axios', () => {
  const instance = { get, put, post: vi.fn(), delete: vi.fn(), interceptors: { request: { use: vi.fn() } } };
  return { default: { create: () => instance, isAxiosError: () => false } };
});

import { adminService } from './adminService';

describe('adminService hata davranışı', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('sağlık raporu alınamazsa uydurma "Healthy" üretmez, hata fırlatır', async () => {
    get.mockRejectedValue(new Error('network'));
    await expect(adminService.getBotDiagnostics()).rejects.toThrow();
    await expect(adminService.getBotHistory()).rejects.toThrow();
  });

  it('bayrak kaydı başarısızsa localStorage\'a yazıp başarı bildirmez', async () => {
    put.mockRejectedValue(new Error('404'));
    await expect(adminService.updateFeatureFlag('canvas', { isEnabled: false })).rejects.toThrow();
    expect(localStorage.getItem('namines_admin_features')).toBeNull();
  });
});
