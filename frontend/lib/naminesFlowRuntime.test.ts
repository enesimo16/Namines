import { describe, it, expect } from 'vitest';
import { buildContext, eventTableIds, matchRules, toastMessageFor } from './naminesFlowRuntime';
import type { AutomationRule } from '../store/useAutomationStore';

const rule = (over: Partial<AutomationRule> = {}): AutomationRule => ({
  id: 'r1',
  scopeTableId: 't1',
  name: '',
  triggerType: 'TableDeleted',
  conditions: [],
  actions: [{ actionType: 'Toast', actionConfig: {} }],
  enabled: true,
  ...over,
});

describe('eventTableIds', () => {
  it('tablo/kolon olaylarinda tek tablo dondurur', () => {
    expect(eventTableIds({ type: 'TableAdded', tableId: 't1', tableName: 'Orders' })).toEqual(['t1']);
    expect(
      eventTableIds({ type: 'ColumnAdded', tableId: 't2', columnId: 'c1', columnName: 'email' }),
    ).toEqual(['t2']);
  });

  it('iliski olaylarinda hem kaynak hem hedef tabloyu dondurur', () => {
    expect(
      eventTableIds({ type: 'RelationAdded', relationId: 'rel1', sourceTableId: 'a', targetTableId: 'b' }),
    ).toEqual(['a', 'b']);
  });
});

describe('matchRules', () => {
  it('tetikleyici ve tablo uyusan kurali dondurur', () => {
    const rules = [rule()];
    const matched = matchRules({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' }, rules);
    expect(matched).toHaveLength(1);
  });

  it('farkli tetikleyiciyi eslestirmez', () => {
    const matched = matchRules({ type: 'TableAdded', tableId: 't1', tableName: 'Orders' }, [rule()]);
    expect(matched).toHaveLength(0);
  });

  it('farkli tabloyu eslestirmez', () => {
    const matched = matchRules({ type: 'TableDeleted', tableId: 'BASKA', tableName: 'X' }, [rule()]);
    expect(matched).toHaveLength(0);
  });

  it('devre disi kurali eslestirmez', () => {
    const matched = matchRules(
      { type: 'TableDeleted', tableId: 't1', tableName: 'Orders' },
      [rule({ enabled: false })],
    );
    expect(matched).toHaveLength(0);
  });

  it('iliski olayi TABLOYA BAGLI kurali eslestirmiyor', () => {
    // Sunucudaki AutomationRuleMatcher ilişki olaylarını yalnızca proje geneli
    // kurallarda değerlendiriyor (diff ilişkileri bir tabloya atfetmiyor).
    // İstemci kaynak/hedef tabloya bağlı kuralları da eşleştirseydi, aynı
    // kural tarayıcıda tetiklenip sunucuda sessiz kalırdı.
    const matched = matchRules(
      { type: 'RelationAdded', relationId: 'rel1', sourceTableId: 'a', targetTableId: 't1' },
      [rule({ triggerType: 'RelationAdded' })],
    );
    expect(matched).toHaveLength(0);
  });

  it('iliski olayi PROJE GENELI kurali eslestiriyor', () => {
    const matched = matchRules(
      { type: 'RelationAdded', relationId: 'rel1', sourceTableId: 'a', targetTableId: 'b' },
      [rule({ triggerType: 'RelationAdded', scopeTableId: '' })],
    );
    expect(matched).toHaveLength(1);
  });

  it('proje geneli kural (bos kapsam) her tabloyla eslesir', () => {
    const matched = matchRules(
      { type: 'TableDeleted', tableId: 'baska-tablo', tableName: 'X' },
      [rule({ scopeTableId: '' })],
    );
    expect(matched).toHaveLength(1);
  });

  it('kolon adi kosuluna uymayan olay kurali tetiklemez', () => {
    const matched = matchRules(
      { type: 'ColumnDeleted', tableId: 't1', columnId: 'c1', columnName: 'title' },
      [rule({
        triggerType: 'ColumnDeleted',
        conditions: [{ field: 'columnName', op: 'endsWith', value: '_id' }],
      })],
    );
    expect(matched).toHaveLength(0);
  });

  it('kolon adi kosuluna uyan olay kurali tetikler', () => {
    const matched = matchRules(
      { type: 'ColumnDeleted', tableId: 't1', columnId: 'c1', columnName: 'user_id' },
      [rule({
        triggerType: 'ColumnDeleted',
        conditions: [{ field: 'columnName', op: 'endsWith', value: '_id' }],
      })],
    );
    expect(matched).toHaveLength(1);
  });

  it('tablo adi kosulu artik istemcide de degerlendirilebiliyor', () => {
    // Olay tabloyu ID ile taşıyor; ad çözücü verildiğinde koşul çalışıyor ve
    // sunucuyla aynı sonucu veriyor.
    const resolve = (id: string) => (id === 't1' ? 'orders' : undefined);
    const rules = [rule({ conditions: [{ field: 'tableName', op: 'equals', value: 'orders' }] })];
    const event = { type: 'TableDeleted', tableId: 't1', tableName: 'orders' } as const;

    expect(matchRules(event, rules, buildContext(event, resolve))).toHaveLength(1);

    const other = [rule({ conditions: [{ field: 'tableName', op: 'equals', value: 'users' }] })];
    expect(matchRules(event, other, buildContext(event, resolve))).toHaveLength(0);
  });

  it('istemcide degerlendirilemeyen kosul kurali DUSURMEZ', () => {
    // `columnType` olay icinde yok. Yanlis susmaktansa fazladan gostermek
    // daha az zararli — sunucu tarafi dogru degerlendiriyor.
    const matched = matchRules(
      { type: 'ColumnAdded', tableId: 't1', columnId: 'c1', columnName: 'total' },
      [rule({
        triggerType: 'ColumnAdded',
        conditions: [{ field: 'columnType', op: 'equals', value: 'INT' }],
      })],
    );
    expect(matched).toHaveLength(1);
  });

  it('ayni olaya bagli birden fazla kurali birlikte dondurur', () => {
    const matched = matchRules({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' }, [
      rule({ id: 'r1' }),
      rule({ id: 'r2', actions: [{ actionType: 'Webhook', actionConfig: {} }] }),
    ]);
    expect(matched.map(r => r.id)).toEqual(['r1', 'r2']);
  });
});

describe('toastMessageFor', () => {
  it('tablo adini mesaja koyar', () => {
    expect(toastMessageFor({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' })).toContain('Orders');
  });

  it('kolon adini mesaja koyar', () => {
    expect(
      toastMessageFor({ type: 'ColumnChanged', tableId: 't1', columnId: 'c1', columnName: 'email' }),
    ).toContain('email');
  });

  it('her olay tipi icin bos olmayan bir mesaj uretir', () => {
    expect(
      toastMessageFor({ type: 'RelationDeleted', relationId: 'r', sourceTableId: 'a', targetTableId: 'b' }),
    ).not.toBe('');
  });
});
