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
      id, scopeTableId: 't-orders', name: '', triggerType: 'TableDeleted', conditions: [],
      actions: [{ actionType: 'Webhook', actionConfig: {} }], enabled: true,
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

    useAutomationStore.getState().updateRule(id, { actions: [{ actionType: 'Webhook', actionConfig: { url: 'https://example.com/hook' } }] });

    const rules = useAutomationStore.getState().rules;
    expect(rules.find(r => r.id === id)?.actions[0].actionConfig).toEqual({ url: 'https://example.com/hook' });
    expect(rules.find(r => r.id === otherId)?.actions[0].actionConfig).toEqual({});
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
      { id: 'r1', scopeTableId: 't1', name: '', triggerType: 'TableDeleted', conditions: [], actions: [{ actionType: 'Webhook', actionConfig: {} }], enabled: true },
    ]);

    await useAutomationStore.getState().loadRules('proj-1');

    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });

  it('ağ hatasında mevcut state i koruyor', async () => {
    vi.mocked(fetchAutomationRules).mockRejectedValue(new Error('network'));
    useAutomationStore.setState({ rules: [{ id: 'r1', scopeTableId: 't1', name: '', triggerType: 'TableAdded', conditions: [], actions: [{ actionType: 'Toast', actionConfig: {} }], enabled: true }], selectedRuleId: null });

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
    name: '', triggerType: 'TableDeleted' as const, conditions: [],
    actions: [{ actionType: 'Toast' as const, actionConfig: {} }], enabled: true,
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
    useAutomationStore.getState().updateRule(SERVER_ID, { actions: [{ actionType: 'Webhook', actionConfig: {} }] });
    useAutomationStore.getState().updateRule(SERVER_ID, {
      actions: [{ actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } }],
    });

    // C2: updateRule artık SUNUCUYA da yazıyor — hem de birleştirilmiş nihai
    // hâliyle (ikinci çağrı ilk çağrının aksiyon tipini korumalı).
    expect(updateAutomationRule).toHaveBeenLastCalledWith(SERVER_ID, expect.objectContaining({
      triggerType: 'TableDeleted',
      actions: [{ actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } }],
      enabled: true,
    }));
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
  });
});

/**
 * FIX ROUND 2 (updateRule/addRule yarış koşulu): `addRule`'un create isteği
 * SUNUCUDAN yanıt almadan (yerel id → sunucu id takası olmadan) hemen
 * ardından `updateRule`/`deleteRule` aynı (yerel) id ile çağrılırsa, arka
 * plandaki PUT/DELETE artık create çözülene kadar ERTELENİYOR ve sunucunun
 * hiç görmediği yerel id yerine GERÇEK sunucu id'siyle gidiyor. Create
 * başarısız olursa (sunucuda satır hiç oluşmadı) PUT/DELETE tamamen
 * atlanıyor — mevcut dosyanın sessiz-başarısızlık deseniyle tutarlı.
 *
 * Senaryo: canvas bağlam menüsünden kural oluşturup HEMEN ardından çekmeceyi
 * açıp trigger/action/webhook URL'i düzenlemek (create'in POST'u henüz ağdan
 * dönmeden).
 */
describe('useAutomationStore FIX ROUND 2: updateRule/deleteRule create ile yarışıyor', () => {
  const SERVER_ID = 'server-side-uuid-race';

  const serverRule = () => ({
    id: SERVER_ID, scopeTableId: 't-orders',
    name: '', triggerType: 'TableDeleted' as const, conditions: [],
    actions: [{ actionType: 'Toast' as const, actionConfig: {} }], enabled: true,
  });

  /** Manuel kontrol edilebilir (deferred) promise: create'i "sunucudan yanıt beklerken" durumunda dondurmak için. */
  const deferred = <T,>() => {
    let resolve!: (value: T) => void;
    let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
  };

  beforeEach(async () => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
    vi.mocked(createAutomationRule).mockReset();
    vi.mocked(updateAutomationRule).mockReset();
    vi.mocked(deleteAutomationRule).mockReset();
    vi.mocked(fetchAutomationRules).mockReset();

    vi.mocked(fetchAutomationRules).mockResolvedValue([]);
    await useAutomationStore.getState().loadRules('proj-42');
  });

  it('create çözülmeden ÖNCE updateRule çağrılırsa: PUT yerel id ile HEMEN atılmıyor, create çözülünce SUNUCU id ile atılıyor', async () => {
    const { promise, resolve } = deferred<ReturnType<typeof serverRule>>();
    vi.mocked(createAutomationRule).mockReturnValue(promise);

    const localId = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');

    // Create HENÜZ sunucudan dönmedi — çekmece hemen açılıp düzenleniyor.
    useAutomationStore.getState().updateRule(localId, { actions: [{ actionType: 'Webhook', actionConfig: {} }] });
    useAutomationStore.getState().updateRule(localId, { actions: [{ actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } }] });

    // Yerel state senkron güncellendi (drawer anında yansımalı).
    expect(useAutomationStore.getState().rules[0]).toMatchObject({
      actions: [{ actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } }],
    });

    // Ama create henüz çözülmedi: PUT hiç atılmamış olmalı — ne yerel id ile
    // (var olmayan bir sunucu satırına 404), ne başka bir id ile.
    expect(updateAutomationRule).not.toHaveBeenCalled();

    // Create şimdi sunucu id'siyle çözülüyor.
    resolve(serverRule());

    await vi.waitFor(() => {
      expect(updateAutomationRule).toHaveBeenCalled();
    });

    // PUT SUNUCU id'siyle, en son birleştirilmiş (nihai) alanlarla atıldı.
    expect(updateAutomationRule).toHaveBeenCalledWith(SERVER_ID, expect.objectContaining({
      triggerType: 'TableDeleted',
      actions: [{ actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } }],
      enabled: true,
    }));
    expect(updateAutomationRule).not.toHaveBeenCalledWith(localId, expect.anything());
  });

  it('create BAŞARISIZ olursa: create çözülmeden önce çağrılan updateRule hiçbir zaman updateAutomationRule tetiklemiyor', async () => {
    const { promise, reject } = deferred<ReturnType<typeof serverRule>>();
    vi.mocked(createAutomationRule).mockReturnValue(promise);

    const localId = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().updateRule(localId, { actions: [{ actionType: 'Webhook', actionConfig: {} }] });

    expect(updateAutomationRule).not.toHaveBeenCalled();

    // Create sunucuda başarısız oluyor (ör. ağ hatası / 500).
    reject(new Error('create failed'));

    // Reddin işlenmesi için event loop'a bir tur ver.
    await new Promise(r => setTimeout(r, 0));
    await new Promise(r => setTimeout(r, 0));

    // Sunucuda hiç satır yok — PUT ASLA çağrılmamalı.
    expect(updateAutomationRule).not.toHaveBeenCalled();
  });

  it('create çözülmeden ÖNCE deleteRule çağrılırsa: DELETE create çözülünce SUNUCU id ile atılıyor', async () => {
    const { promise, resolve } = deferred<ReturnType<typeof serverRule>>();
    vi.mocked(createAutomationRule).mockReturnValue(promise);

    const localId = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().deleteRule(localId);

    // Yerel state senkron güncellendi: kural hemen kayboldu.
    expect(useAutomationStore.getState().rules).toHaveLength(0);
    expect(deleteAutomationRule).not.toHaveBeenCalled();

    resolve(serverRule());

    await vi.waitFor(() => {
      expect(deleteAutomationRule).toHaveBeenCalled();
    });

    expect(deleteAutomationRule).toHaveBeenCalledWith(SERVER_ID);
    expect(deleteAutomationRule).not.toHaveBeenCalledWith(localId);
  });
});
