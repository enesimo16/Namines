import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * `localforage` (IndexedDB) Node ortamında yok ve store onu persist katmanı
 * için modül düzeyinde import ediyor. Taklit edilmezse dosyanın import'u
 * patlar — yani testin konusu olmayan bir bağımlılık, testi imkânsız kılar.
 */
vi.mock('localforage', () => ({
  default: {
    getItem: vi.fn().mockResolvedValue(null),
    setItem: vi.fn().mockResolvedValue(undefined),
    removeItem: vi.fn().mockResolvedValue(undefined),
    config: vi.fn(),
  },
}));

const { useSchemaStore } = await import('./useSchemaStore');

/**
 * `useSchemaStore` — ürünün ÇEKİRDEK durumu (FE-001 / TD-001 / B-15).
 *
 * Denetim "frontend store'ları test edilmemiş" diyordu ve bu store en ağır
 * mantığı taşıyor: ilişki kurma kuralları, proje adı koruma kararı, geri al.
 * Buradaki sessiz bir gerileme, kullanıcının şemasını bozar — ve şema bu
 * ürünün ürettiği şeyin tamamı.
 */
const column = (id: string, name: string, isPK = false) => ({
  id, name, type: 'int', isPK, isFK: false, isNullable: false,
});

const table = (id: string, name: string, cols: ReturnType<typeof column>[]) => ({
  id, name, columns: cols,
});

const schemaWith = (name = 'Shop') => ({
  name,
  tables: [
    table('t-users', 'users', [column('c-users-id', 'id', true)]),
    table('t-orders', 'orders', [column('c-orders-id', 'id', true), column('c-orders-user', 'user_id')]),
  ],
  relations: [] as unknown[],
});

describe('useSchemaStore — connectColumns kuralları', () => {
  beforeEach(() => {
    useSchemaStore.setState({ schema: null, nodes: [], edges: [], projectName: 'Untitled Schema' } as never);
  });

  it('şema yoksa ilişki kurulmaz', () => {
    const result = useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never);

    expect(result.ok).toBe(false);
    expect(result.reason).toBe('No schema is loaded.');
  });

  it('kolon seçilmemişse açıklayıcı hata döner', () => {
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const result = useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: null, targetHandle: null,
    } as never);

    expect(result.ok).toBe(false);
    expect(result.reason).toContain('source and a target column');
  });

  it('tablo kendisine bağlanamaz', () => {
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const result = useSchemaStore.getState().connectColumns({
      source: 't-users', target: 't-users', sourceHandle: 'c-users-id', targetHandle: 'c-users-id',
    } as never);

    expect(result.ok).toBe(false);
    expect(result.reason).toContain('itself');
  });

  /**
   * FK yalnızca BİRİNCİL ANAHTARI işaret edebilir. Bu kural düşerse üretilen
   * DDL, veritabanının reddedeceği bir yabancı anahtar içerir — ve hata
   * kullanıcıya ancak çalıştırma anında, anlaşılmaz bir motor mesajı olarak
   * döner.
   */
  it('hedef birincil anahtar değilse reddedilir', () => {
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const result = useSchemaStore.getState().connectColumns({
      source: 't-users', target: 't-orders', sourceHandle: 'c-users-id', targetHandle: 'c-orders-user',
    } as never);

    expect(result.ok).toBe(false);
    expect(result.reason).toContain('primary key');
  });

  it('geçerli ilişki kurulur ve kaynak kolon FK olarak işaretlenir', () => {
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const result = useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never);

    expect(result.ok).toBe(true);

    const schema = useSchemaStore.getState().schema!;
    expect(schema.relations).toHaveLength(1);

    const orders = schema.tables.find(t => t.id === 't-orders')!;
    const fkColumn = orders.columns.find(c => c.id === 'c-orders-user')!;
    // Bayrak DDL üretiminin girdisi; kurulmazsa ilişki şemada var ama SQL'de yok.
    expect(fkColumn.isFK).toBe(true);
  });

  it('aynı ilişki ikinci kez eklenmez', () => {
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);
    const connection = {
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never;

    useSchemaStore.getState().connectColumns(connection);
    const second = useSchemaStore.getState().connectColumns(connection);

    expect(second.ok).toBe(false);
    expect(second.reason).toBe('This relation already exists.');
    expect(useSchemaStore.getState().schema!.relations).toHaveLength(1);
  });
});

describe('useSchemaStore — proje adı koruma kararı', () => {
  beforeEach(() => {
    useSchemaStore.setState({ schema: null, nodes: [], edges: [], projectName: 'Untitled Schema' } as never);
  });

  /**
   * Kullanıcı projesine kendi adını verdiyse, bir şema yüklemek onu
   * EZMEMELİ — 40 tablolu bir şablon yükledikten sonra üstte hâlâ eski adın
   * yazması da, kullanıcının koyduğu adın sessizce kaybolması da yanlış.
   * Varsayılan davranış: özel ad korunur.
   */
  it('kullanıcının verdiği özel ad varsayılan olarak korunur', () => {
    useSchemaStore.setState({ projectName: 'Benim Projem' } as never);

    useSchemaStore.getState().loadFromSchema(schemaWith('E-Commerce') as never);

    expect(useSchemaStore.getState().projectName).toBe('Benim Projem');
  });

  /**
   * REGRESYON (bu testi yazarken bulundu): `app/canvas/page.tsx` sıfırdan
   * başlatılan şemaya 'Untitled Schema' adını veriyor, ama bu ad yer tutucu
   * listesinde YOKTU — yani "kullanıcının verdiği özel ad" sayılıyordu ve
   * sonradan yüklenen bir ŞABLONUN adı alınmıyordu. Kullanıcı 40 tablolu
   * şablonu yüklüyor, başlıkta hâlâ 'Untitled Schema' yazıyordu.
   * Düzeltme: ad `PLACEHOLDER_PROJECT_NAMES`'e eklendi.
   */
  it('ad yer tutucuysa şemanın adı alınır', () => {
    useSchemaStore.setState({ projectName: 'Untitled Schema' } as never);

    useSchemaStore.getState().loadFromSchema(schemaWith('E-Commerce') as never);

    expect(useSchemaStore.getState().projectName).toBe('E-Commerce');
  });

  /**
   * `preserveProjectName: false` AÇIKÇA "adı da değiştir" demek — şablon
   * galerisindeki "Replace" bunu kullanıyor.
   */
  it('preserveProjectName false ise özel ad bile değişir', () => {
    useSchemaStore.setState({ projectName: 'Benim Projem' } as never);

    useSchemaStore.getState().loadFromSchema(schemaWith('E-Commerce') as never, undefined, false);

    expect(useSchemaStore.getState().projectName).toBe('E-Commerce');
  });

  /**
   * Argüman MUTATE EDİLMEMELİ: çağıranlar buraya kendilerine ait olmayan
   * nesneler veriyor (kayıtlı proje, ağdan gelen multiplayer nesnesi).
   * Mutasyon o store'ların verisini `set()` dışından bozardı.
   */
  it('verilen şema nesnesi değiştirilmez', () => {
    const incoming = schemaWith('E-Commerce');
    useSchemaStore.setState({ projectName: 'Benim Projem' } as never);

    useSchemaStore.getState().loadFromSchema(incoming as never);

    expect(incoming.name).toBe('E-Commerce');
  });

  it('verilen düğüm konumları korunur', () => {
    useSchemaStore.getState().loadFromSchema(
      schemaWith() as never,
      { 't-users': { x: 123, y: 456 } } as never,
    );

    const node = useSchemaStore.getState().nodes.find(n => n.id === 't-users')!;
    expect(node.position).toEqual({ x: 123, y: 456 });
  });
});

describe('useSchemaStore — geri al / yinele', () => {
  beforeEach(() => {
    useSchemaStore.setState({ schema: null, nodes: [], edges: [], _past: [], _future: [] } as never);
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);
  });

  it('ilişki kurulduktan sonra geri alınabilir', () => {
    expect(useSchemaStore.getState().canUndo()).toBe(false);

    useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never);
    expect(useSchemaStore.getState().schema!.relations).toHaveLength(1);
    expect(useSchemaStore.getState().canUndo()).toBe(true);

    useSchemaStore.getState().undo();

    expect(useSchemaStore.getState().schema!.relations).toHaveLength(0);
  });

  it('geri alınan işlem yinelenebilir', () => {
    useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never);
    useSchemaStore.getState().undo();

    expect(useSchemaStore.getState().canRedo()).toBe(true);
    useSchemaStore.getState().redo();

    expect(useSchemaStore.getState().schema!.relations).toHaveLength(1);
  });
});
