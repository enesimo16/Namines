import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAutomationStore } from './useAutomationStore';

describe('useAutomationStore', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
  });

  it('addRule bir kural ekler ve id döner', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');

    const rules = useAutomationStore.getState().rules;
    expect(rules).toHaveLength(1);
    expect(rules[0]).toMatchObject({
      id, scopeTableId: 't-orders', triggerType: 'TableDeleted', actionType: 'Webhook',
      actionConfig: {}, enabled: true,
    });
  });

  it('rulesForTable yalnızca o tabloya ait kuralları döner', () => {
    useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().addRule('t-users', 'ColumnAdded', 'Toast');

    const ordersRules = useAutomationStore.getState().rulesForTable('t-orders');
    expect(ordersRules).toHaveLength(1);
    expect(ordersRules[0].scopeTableId).toBe('t-orders');
  });

  it('updateRule yalnızca hedeflenen kuralı, verilen alanları değiştirir', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    const otherId = useAutomationStore.getState().addRule('t-users', 'ColumnAdded', 'Toast');

    useAutomationStore.getState().updateRule(id, { actionConfig: { url: 'https://example.com/hook' } });

    const rules = useAutomationStore.getState().rules;
    expect(rules.find(r => r.id === id)?.actionConfig).toEqual({ url: 'https://example.com/hook' });
    expect(rules.find(r => r.id === otherId)?.actionConfig).toEqual({});
  });

  it('deleteRule yalnızca hedeflenen kuralı kaldırır', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    const otherId = useAutomationStore.getState().addRule('t-users', 'ColumnAdded', 'Toast');

    useAutomationStore.getState().deleteRule(id);

    const rules = useAutomationStore.getState().rules;
    expect(rules).toHaveLength(1);
    expect(rules[0].id).toBe(otherId);
  });

  it('deleteRulesForTable o tabloya ait TÜM kuralları kaldırır', () => {
    useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().addRule('t-orders', 'ColumnAdded', 'Toast');
    useAutomationStore.getState().addRule('t-users', 'ColumnAdded', 'Toast');

    useAutomationStore.getState().deleteRulesForTable('t-orders');

    const rules = useAutomationStore.getState().rules;
    expect(rules).toHaveLength(1);
    expect(rules[0].scopeTableId).toBe('t-users');
  });

  it('setSelectedRuleId çekmecenin hangi kuralı düzenlediğini tutar', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');

    useAutomationStore.getState().setSelectedRuleId(id);
    expect(useAutomationStore.getState().selectedRuleId).toBe(id);

    useAutomationStore.getState().setSelectedRuleId(null);
    expect(useAutomationStore.getState().selectedRuleId).toBeNull();
  });
});

vi.mock('../lib/automationApi', () => ({
  fetchAutomationRules: vi.fn(),
  createAutomationRule: vi.fn(),
  deleteAutomationRule: vi.fn(),
}));

import { fetchAutomationRules } from '../lib/automationApi';

describe('useAutomationStore.loadRules', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
    vi.mocked(fetchAutomationRules).mockReset();
  });

  it('sunucudan gelen kuralları state e yazıyor', async () => {
    vi.mocked(fetchAutomationRules).mockResolvedValue([
      { id: 'r1', scopeTableId: 't1', triggerType: 'TableDeleted', actionType: 'Webhook', actionConfig: {}, enabled: true },
    ]);

    await useAutomationStore.getState().loadRules('proj-1');

    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });

  it('ağ hatasında mevcut state i koruyor', async () => {
    vi.mocked(fetchAutomationRules).mockRejectedValue(new Error('network'));
    useAutomationStore.setState({ rules: [{ id: 'r1', scopeTableId: 't1', triggerType: 'TableAdded', actionType: 'Toast', actionConfig: {}, enabled: true }], selectedRuleId: null });

    await useAutomationStore.getState().loadRules('proj-1');

    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });
});
