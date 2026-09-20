// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';

vi.mock('../../lib/automationApi', () => ({
  fetchRuleRuns: vi.fn(),
  testAutomationRule: vi.fn(),
}));

import AutomationRunHistory from './AutomationRunHistory';
import { fetchRuleRuns, testAutomationRule, type AutomationRun } from '../../lib/automationApi';
import { useToastStore } from '../../store/useToastStore';

const run = (over: Partial<AutomationRun> = {}): AutomationRun => ({
  id: 'run-1',
  ruleId: 'r1',
  triggeredAt: '2026-09-19T10:00:00Z',
  actionType: 'Webhook',
  status: 'Success',
  isTest: false,
  errorMessage: null,
  resultSummary: null,
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  useToastStore.getState().clearAll();
});
afterEach(cleanup);

describe('AutomationRunHistory', () => {
  it('hic calisma yoksa bunu SESSIZ kalmadan soyluyor', async () => {
    vi.mocked(fetchRuleRuns).mockResolvedValue([]);
    render(<AutomationRunHistory ruleId="r1" />);

    expect(await screen.findByText(/hasn’t run on the server yet/i)).toBeInTheDocument();
  });

  it('atlanan calismanin SEBEBINI gosteriyor', async () => {
    // Bu satırın tamamı bu fazın varlık sebebi: bir webhook SSRF kontrolüne
    // takılıp sessizce atlandığında kullanıcı bugüne kadar hiçbir şey
    // göremiyordu.
    vi.mocked(fetchRuleRuns).mockResolvedValue([
      run({ status: 'Skipped', errorMessage: 'Webhook URL is missing or not a safe public target.' }),
    ]);
    render(<AutomationRunHistory ruleId="r1" />);

    expect(await screen.findByText('Skipped')).toBeInTheDocument();
    expect(screen.getByText(/not a safe public target/i)).toBeInTheDocument();
  });

  it('yukleme hatasi bos gecmisten AYIRT EDILIYOR', async () => {
    // Ağ hatasında boş liste göstermek "hiç çalışmadı" demek olurdu.
    vi.mocked(fetchRuleRuns).mockRejectedValue(new Error('network'));
    render(<AutomationRunHistory ruleId="r1" />);

    expect(await screen.findByText(/could not load the run history/i)).toBeInTheDocument();
    expect(screen.queryByText(/hasn’t run on the server yet/i)).not.toBeInTheDocument();
  });

  it('test basarili bitince toast basiyor ve gecmisi yeniliyor', async () => {
    vi.mocked(fetchRuleRuns).mockResolvedValue([]);
    vi.mocked(testAutomationRule).mockResolvedValue({ clientOnlyActions: 0, runs: [run()] });

    render(<AutomationRunHistory ruleId="r1" />);
    await screen.findByText(/hasn’t run on the server yet/i);

    fireEvent.click(screen.getByRole('button', { name: /test now/i }));

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(t => /every server-side step succeeded/i.test(t.message))).toBe(true);
    });
    // İlk yükleme + testten sonraki yenileme.
    expect(fetchRuleRuns).toHaveBeenCalledTimes(2);
  });

  it('basarisiz adim varsa hata toastu basiyor', async () => {
    vi.mocked(fetchRuleRuns).mockResolvedValue([]);
    vi.mocked(testAutomationRule).mockResolvedValue({
      clientOnlyActions: 0,
      runs: [run({ status: 'Failed', errorMessage: 'Webhook returned 500.' })],
    });

    render(<AutomationRunHistory ruleId="r1" />);
    fireEvent.click(await screen.findByRole('button', { name: /test now/i }));

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(t => /1 step\(s\) failed/i.test(t.message))).toBe(true);
    });
  });

  it('yalnizca istemci aksiyonu varsa bunu acikca soyluyor', async () => {
    // Aksi hâlde "test ettim, hiçbir sonuç yok" gibi görünürdü.
    vi.mocked(fetchRuleRuns).mockResolvedValue([]);
    vi.mocked(testAutomationRule).mockResolvedValue({ clientOnlyActions: 1, runs: [] });

    render(<AutomationRunHistory ruleId="r1" />);
    fireEvent.click(await screen.findByRole('button', { name: /test now/i }));

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(t => /only has client-side actions/i.test(t.message))).toBe(true);
    });
  });

  it('hiz sinirina takilinca ne yapilacagini soyluyor', async () => {
    vi.mocked(fetchRuleRuns).mockResolvedValue([]);
    vi.mocked(testAutomationRule).mockRejectedValue({ response: { status: 429 } });

    render(<AutomationRunHistory ruleId="r1" />);
    fireEvent.click(await screen.findByRole('button', { name: /test now/i }));

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(t => /wait a minute/i.test(t.message))).toBe(true);
    });
  });
});
