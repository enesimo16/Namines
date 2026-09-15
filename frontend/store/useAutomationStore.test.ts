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
  updateAutomationRule: vi.fn(),
  deleteAutomationRule: vi.fn(),
}));

import {
  fetchAutomationRules,
  createAutomationRule,
  updateAutomationRule,
  deleteAutomationRule,
} from '../lib/automationApi';

// NOT: bu describe, dosyadaki `loadRules` çağıran testlerden ÖNCE
// tanımlanmalı/çalışmalı — `currentProjectId` `useAutomationStore.ts`
// içinde MODÜL seviyesinde tutuluyor (store'un dışa açık imzası bir
// projectId alamadığı için), yani `useAutomationStore.setState(...)` onu
// SIFIRLAMIYOR ve testler arasında kalıcı. İlk testin "hiç loadRules
// çalışmadı" durumunu doğru sınayabilmesi için dosyadaki başka hiçbir
// testin ondan önce `loadRules` çağırmamış olması gerekiyor.
describe('useAutomationStore.addRule arka plan API çağrısı (projectId eşleme)', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
    vi.mocked(createAutomationRule).mockReset();
    vi.mocked(fetchAutomationRules).mockReset();
  });

  it('hiç loadRules çalışmadıysa createAutomationRule ÇAĞRILMAZ (kural yerel kalır)', () => {
    useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');

    expect(createAutomationRule).not.toHaveBeenCalled();
    // Yine de kural yerelde optimistik olarak eklenmiş olmalı.
    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });

  it('loadRules çalıştıktan sonra createAutomationRule doğru projectId ile çağrılır', async () => {
    vi.mocked(fetchAutomationRules).mockResolvedValue([]);
    await useAutomationStore.getState().loadRules('proj-42');

    useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');

    expect(createAutomationRule).toHaveBeenCalledWith('proj-42', 't-orders', 'TableDeleted', 'Webhook');
  });
});

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

/**
 * ZİNCİR TESTİ (C3 + C2 + I3): yerel id → sunucu id takası ve sonrasındaki
 * düzenleme/silme çağrılarının SUNUCU id'sini kullanması.
 *
 * Eskiden `addRule` sunucunun yanıtını yok sayıyordu; istemci `genId()`
 * uuid'siyle kalıyor, sonraki DELETE (ve C2 ile gelen PUT) var olmayan bir
 * id'ye gidip 404 alıyor, hata `.catch(() => {})` içinde yutuluyordu — kural
 * bir sonraki `loadRules`'ta geri geliyordu.
 */
describe('useAutomationStore kural yaşam döngüsü (yerel id → sunucu id)', () => {
  const SERVER_ID = 'server-side-uuid-9999';

  const serverRule = () => ({
    id: SERVER_ID, scopeTableId: 't-orders',
    triggerType: 'TableDeleted' as const, actionType: 'Toast' as const,
    actionConfig: {}, enabled: true,
  });

  beforeEach(async () => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
    vi.mocked(createAutomationRule).mockReset();
    vi.mocked(updateAutomationRule).mockReset();
    vi.mocked(deleteAutomationRule).mockReset();
    vi.mocked(fetchAutomationRules).mockReset();

    vi.mocked(fetchAutomationRules).mockResolvedValue([]);
    await useAutomationStore.getState().loadRules('proj-42');
  });

  it('createAutomationRule çözüldüğünde yerel id sunucu id ile değiştiriliyor', async () => {
    vi.mocked(createAutomationRule).mockResolvedValue(serverRule());

    const localId = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(localId);

    // Senkron dönüş DEĞİŞMEDİ: hâlâ yerel id (dışa açık imza sabit).
    expect(localId).not.toBe(SERVER_ID);
    expect(useAutomationStore.getState().rules[0].id).toBe(localId);

    await vi.waitFor(() => {
      expect(useAutomationStore.getState().rules[0].id).toBe(SERVER_ID);
    });

    // Seçim de taşınmalı, yoksa çekmece düzenleme ortasında seçimi kaybederdi.
    expect(useAutomationStore.getState().selectedRuleId).toBe(SERVER_ID);
  });

  it('takastan sonra updateRule ve deleteRule SUNUCU id ile çağrılıyor', async () => {
    vi.mocked(createAutomationRule).mockResolvedValue(serverRule());

    const localId = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');

    await vi.waitFor(() => {
      expect(useAutomationStore.getState().rules[0].id).toBe(SERVER_ID);
    });

    // Kullanıcı çekmecede aksiyonu Webhook'a çevirip URL giriyor.
    useAutomationStore.getState().updateRule(SERVER_ID, { actionType: 'Webhook' });
    useAutomationStore.getState().updateRule(SERVER_ID, {
      actionConfig: { url: 'https://example.test/hook' },
    });

    // C2: updateRule artık SUNUCUYA da yazıyor — hem de birleştirilmiş nihai
    // hâliyle (ikinci çağrı ilk çağrının actionType'ını korumalı).
    expect(updateAutomationRule).toHaveBeenLastCalledWith(SERVER_ID, {
      triggerType: 'TableDeleted',
      actionType: 'Webhook',
      actionConfig: { url: 'https://example.test/hook' },
      enabled: true,
    });
    expect(updateAutomationRule).not.toHaveBeenCalledWith(localId, expect.anything());

    useAutomationStore.getState().deleteRule(SERVER_ID);

    expect(deleteAutomationRule).toHaveBeenCalledWith(SERVER_ID);
    expect(deleteAutomationRule).not.toHaveBeenCalledWith(localId);
    expect(useAutomationStore.getState().rules).toHaveLength(0);
  });

  it('updateRule yerel state i SENKRON güncelliyor (API çağrısını beklemeden)', () => {
    vi.mocked(createAutomationRule).mockResolvedValue(serverRule());

    const localId = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');

    // Takas HENÜZ olmadan (promise çözülmeden) düzenleme: yerel state anında
    // değişmeli, hiçbir await gerekmemeli.
    useAutomationStore.getState().updateRule(localId, { enabled: false });

    expect(useAutomationStore.getState().rules[0].enabled).toBe(false);
    expect(updateAutomationRule).toHaveBeenCalledWith(localId, expect.objectContaining({ enabled: false }));
  });
});
