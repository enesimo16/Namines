# Namines Flow — Event Kontratı (Spec Bölüm 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Canvas'taki şema mutasyonlarının (tablo/kolon/ilişki
ekleme-silme-değiştirme) **Namines Flow** adlı, açıkça isimlendirilmiş bir
olay veriyoluna anında yayın yapmasını sağlamak — istemci tarafı anlık
tepkilerin (Bölüm 2'de eklenecek toast/görsel ipucu) dayanacağı temel.

**Architecture:** `frontend/store/useSchemaStore.ts`'teki mevcut mutasyon
fonksiyonları (`addTable`, `deleteTable`, `updateTable`, `connectColumns`,
`deleteRelation`) DEĞİŞTİRİLMİYOR — her birinin state'i `set()` ettiği
noktanın hemen ardına, yeni `naminesFlow` pub/sub'ına bir yayın satırı
ekleniyor. Yayın senkron ve yan etki; ağa çıkmıyor.

**Tech Stack:** TypeScript, Zustand, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-14-namines-flow-design.md`
(Bölüm 1)

## Global Constraints

- **İsimlendirme SOYUT olamaz.** Modül, tip ve olay isimleri "Namines
  Flow" markasını açıkça taşımalı — `frontend/lib/naminesFlowEventBus.ts`
  (spec taslağındaki jenerik `flowEventBus.ts` adı yerine), dışa açılan
  tip `NaminesFlowEvent`. Kullanıcının açık talebi: "bu sistemin namines
  flow adında olduğunu göstereceğiz soyutluk yok".
- Mevcut mutasyon davranışı (state şekli, undo/redo `_past`/`_future`)
  BİREBİR korunuyor — yayın satırı yalnızca EKLENIYOR, mevcut mantığın
  hiçbir satırı değişmiyor.
- Yayın state `set()` edildikten SONRA yapılıyor — dinleyiciler tutarlı
  (güncel) state üzerinden çalışsın diye.
- Kolon olayları (`ColumnAdded`/`ColumnDeleted`/`ColumnChanged`) yalnızca
  `updateTable` içinde, eski tablo ile yeni tablo diff'lenerek üretiliyor
  — `updateTable`'ın kendisi kolon-bazlı bir API değil, bu yüzden diff
  şart.

---

### Task 1: `naminesFlowEventBus` — çekirdek pub/sub

**Files:**
- Create: `frontend/lib/naminesFlowEventBus.ts`
- Test: `frontend/lib/naminesFlowEventBus.test.ts`

**Interfaces:**
- Produces:
  ```ts
  export type NaminesFlowEvent =
    | { type: 'TableAdded'; tableId: string; tableName: string }
    | { type: 'TableDeleted'; tableId: string; tableName: string }
    | { type: 'ColumnAdded'; tableId: string; columnId: string; columnName: string }
    | { type: 'ColumnDeleted'; tableId: string; columnId: string; columnName: string }
    | { type: 'ColumnChanged'; tableId: string; columnId: string; columnName: string }
    | { type: 'RelationAdded'; relationId: string; sourceTableId: string; targetTableId: string }
    | { type: 'RelationDeleted'; relationId: string; sourceTableId: string; targetTableId: string };

  export const naminesFlow: {
    emit(event: NaminesFlowEvent): void;
    on(type: NaminesFlowEvent['type'] | '*', listener: (event: NaminesFlowEvent) => void): () => void;
  };
  ```

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect, vi } from 'vitest';
import { naminesFlow, type NaminesFlowEvent } from './naminesFlowEventBus';

describe('naminesFlowEventBus', () => {
  it('delivers an event only to listeners subscribed to that exact type', () => {
    const tableListener = vi.fn();
    const columnListener = vi.fn();
    naminesFlow.on('TableAdded', tableListener);
    naminesFlow.on('ColumnAdded', columnListener);

    naminesFlow.emit({ type: 'TableAdded', tableId: 't1', tableName: 'Orders' });

    expect(tableListener).toHaveBeenCalledTimes(1);
    expect(tableListener).toHaveBeenCalledWith({ type: 'TableAdded', tableId: 't1', tableName: 'Orders' });
    expect(columnListener).not.toHaveBeenCalled();
  });

  it('delivers every event to a wildcard ("*") listener', () => {
    const wildcard = vi.fn();
    naminesFlow.on('*', wildcard);

    const event: NaminesFlowEvent = { type: 'RelationDeleted', relationId: 'r1', sourceTableId: 't1', targetTableId: 't2' };
    naminesFlow.emit(event);

    expect(wildcard).toHaveBeenCalledWith(event);
  });

  it('unsubscribe stops further delivery without touching other listeners', () => {
    const a = vi.fn();
    const b = vi.fn();
    const unsubscribeA = naminesFlow.on('TableDeleted', a);
    naminesFlow.on('TableDeleted', b);

    unsubscribeA();
    naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });

    expect(a).not.toHaveBeenCalled();
    expect(b).toHaveBeenCalledTimes(1);
  });

  it('a listener throwing does not stop delivery to the remaining listeners', () => {
    const throwing = vi.fn(() => { throw new Error('boom'); });
    const after = vi.fn();
    naminesFlow.on('TableAdded', throwing);
    naminesFlow.on('TableAdded', after);

    expect(() =>
      naminesFlow.emit({ type: 'TableAdded', tableId: 't1', tableName: 'Orders' })
    ).not.toThrow();
    expect(after).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run lib/naminesFlowEventBus.test.ts`
Expected: FAIL — `naminesFlowEventBus.ts` does not exist (import error).

- [ ] **Step 3: Write minimal implementation**

```ts
/**
 * Namines Flow — Namines'in olay-tabanlı otomasyon katmanının çekirdek
 * olay veriyolu.
 *
 * Canvas'taki bir şema mutasyonu (tablo/kolon/ilişki ekleme-silme-değiştirme)
 * burada bir olay olarak yayınlanır. Bu modül YALNIZCA aynı tarayıcı
 * sekmesindeki ANLIK tepkiler içindir (toast, görsel ipucu) — ağa hiç
 * çıkmaz. Kalıcı/sunucu tarafı otomasyon (webhook, DBA kontrolü, örnek veri
 * üretimi) ayrı bir yoldan, sunucunun kendi hesapladığı şema diff'inden
 * tetiklenir (bkz. docs/superpowers/specs/2026-09-14-namines-flow-design.md
 * Bölüm 1 "Sunucu tarafı").
 */

export type NaminesFlowEvent =
  | { type: 'TableAdded'; tableId: string; tableName: string }
  | { type: 'TableDeleted'; tableId: string; tableName: string }
  | { type: 'ColumnAdded'; tableId: string; columnId: string; columnName: string }
  | { type: 'ColumnDeleted'; tableId: string; columnId: string; columnName: string }
  | { type: 'ColumnChanged'; tableId: string; columnId: string; columnName: string }
  | { type: 'RelationAdded'; relationId: string; sourceTableId: string; targetTableId: string }
  | { type: 'RelationDeleted'; relationId: string; sourceTableId: string; targetTableId: string };

type Listener = (event: NaminesFlowEvent) => void;
type ListenerKey = NaminesFlowEvent['type'] | '*';

function createNaminesFlowEventBus() {
  const listeners = new Map<ListenerKey, Set<Listener>>();

  function on(key: ListenerKey, listener: Listener): () => void {
    if (!listeners.has(key)) listeners.set(key, new Set());
    listeners.get(key)!.add(listener);
    return () => {
      listeners.get(key)?.delete(listener);
    };
  }

  function emit(event: NaminesFlowEvent): void {
    const targets = new Set<Listener>([
      ...(listeners.get(event.type) ?? []),
      ...(listeners.get('*') ?? []),
    ]);

    for (const listener of targets) {
      try {
        listener(event);
      } catch (err) {
        // Bir dinleyicinin patlaması diğerlerinin çalışmasını engellememeli —
        // örn. bozuk bir üçüncü parti entegrasyon, kullanıcının kendi
        // toast'unu görmesini engellemesin.
        console.error('[Namines Flow] listener threw:', err);
      }
    }
  }

  return { emit, on };
}

export const naminesFlow = createNaminesFlowEventBus();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run lib/naminesFlowEventBus.test.ts`
Expected: PASS (4/4)

- [ ] **Step 5: Commit**

```bash
git add frontend/lib/naminesFlowEventBus.ts frontend/lib/naminesFlowEventBus.test.ts
git commit -m "feat: add Namines Flow core event bus"
```

---

### Task 2: Tablo olayları — `addTable`/`deleteTable`

**Files:**
- Modify: `frontend/store/useSchemaStore.ts:334-402` (`addTable`, `deleteTable`)
- Test: `frontend/store/useSchemaStore.test.ts` (append)

**Interfaces:**
- Consumes: `naminesFlow.emit` (Task 1).

- [ ] **Step 1: Write the failing test**

Append to `frontend/store/useSchemaStore.test.ts`:

```ts
describe('useSchemaStore — Namines Flow olayları', () => {
  beforeEach(() => {
    useSchemaStore.setState({ schema: null, nodes: [], edges: [], projectName: 'Untitled Schema' } as never);
  });

  it('addTable "TableAdded" olayını yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('TableAdded', listener);
    useSchemaStore.getState().addTable(100, 100);
    unsubscribe();

    expect(listener).toHaveBeenCalledTimes(1);
    const [event] = listener.mock.calls[0];
    expect(event.type).toBe('TableAdded');
    expect(event.tableName).toBe('Yeni_Tablo_3'); // schemaWith() 2 tablo veriyor
  });

  it('deleteTable "TableDeleted" olayını yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('TableDeleted', listener);
    useSchemaStore.getState().deleteTable('t-orders');
    unsubscribe();

    expect(listener).toHaveBeenCalledWith({ type: 'TableDeleted', tableId: 't-orders', tableName: 'orders' });
  });

  it('şema yokken addTable/deleteTable hiçbir olay yayınlamaz', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('*', listener);

    useSchemaStore.getState().addTable(0, 0);
    useSchemaStore.getState().deleteTable('nope');
    unsubscribe();

    expect(listener).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts -t "Namines Flow"`
Expected: FAIL — henüz hiçbir yayın yok, `listener` hiç çağrılmaz.

- [ ] **Step 3: Write minimal implementation**

`frontend/store/useSchemaStore.ts`'nin en üstüne import ekle:

```ts
import { naminesFlow } from '../lib/naminesFlowEventBus';
```

`addTable`'ın SONUNDA (mevcut `set({...})` çağrısından HEMEN SONRA, fonksiyon gövdesinin son satırı olarak):

```ts
        set({
          schema: newSchema,
          nodes: [...state.nodes, newNode],
          // edges değişmez
        });

        naminesFlow.emit({ type: 'TableAdded', tableId: newTableId, tableName: newTable.name });
      },
```

`deleteTable`'ın SONUNDA — ama silinen tablonun adını `set()`'ten ÖNCE,
`state.schema.tables`'tan bulmak gerekiyor (filtrelendikten sonra artık
yok):

```ts
      deleteTable: (tableId) => {
        const state = get();
        if (!state.schema) return;
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        const deletedTable = state.schema.tables.find(t => t.id === tableId);

        const newTables = state.schema.tables.filter(t => t.id !== tableId);
        // ... (mevcut kod değişmeden devam ediyor) ...

        set({
          schema: newSchema,
          nodes: state.nodes.filter(n => n.id !== tableId),
          edges: state.edges.filter(e => !relatedRelIds.includes(e.id)),
          selectedTableForEdit: state.selectedTableForEdit === tableId ? null : state.selectedTableForEdit,
        });

        if (deletedTable) {
          naminesFlow.emit({ type: 'TableDeleted', tableId, tableName: deletedTable.name });
        }
      },
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts -t "Namines Flow"`
Expected: PASS (3/3)

- [ ] **Step 5: Run the full store test file to confirm no regression**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts`
Expected: all pre-existing tests still PASS — the diff only appends code
after each function's existing `set()` call.

- [ ] **Step 6: Commit**

```bash
git add frontend/store/useSchemaStore.ts frontend/store/useSchemaStore.test.ts
git commit -m "feat: emit Namines Flow TableAdded/TableDeleted events"
```

---

### Task 3: Kolon olayları — `updateTable` diff'i

**Files:**
- Modify: `frontend/store/useSchemaStore.ts:446-480` (`updateTable`)
- Test: `frontend/store/useSchemaStore.test.ts` (append)

**Interfaces:**
- Consumes: `naminesFlow.emit` (Task 1).
- Produces: iç yardımcı `diffColumnsForFlowEvents(oldTable, newTable):
  NaminesFlowEvent[]` (dışa aktarılmaz, yalnızca bu dosya içinde
  kullanılır — ama isim testte referans olarak kullanılacağı için
  sabit tutulmalı).

**Not:** `updateTable` tek bir çağrıda birden fazla kolon
ekleyebilir/silebilir/değiştirebilir (drawer'da kullanıcı birkaç alanı
aynı anda değiştirip kaydedebilir) — bu yüzden diff **listesi** üretiyor,
tek bir olay değil.

- [ ] **Step 1: Write the failing test**

Append to `frontend/store/useSchemaStore.test.ts`:

```ts
  it('updateTable yeni bir kolon eklendiğinde "ColumnAdded" yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('ColumnAdded', listener);

    const ordersTable = useSchemaStore.getState().schema!.tables.find(t => t.id === 't-orders')!;
    useSchemaStore.getState().updateTable({
      ...ordersTable,
      columns: [...ordersTable.columns, column('c-orders-total', 'total')],
    } as never);
    unsubscribe();

    expect(listener).toHaveBeenCalledWith({
      type: 'ColumnAdded', tableId: 't-orders', columnId: 'c-orders-total', columnName: 'total',
    });
  });

  it('updateTable bir kolon kaldırıldığında "ColumnDeleted" yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('ColumnDeleted', listener);

    const ordersTable = useSchemaStore.getState().schema!.tables.find(t => t.id === 't-orders')!;
    useSchemaStore.getState().updateTable({
      ...ordersTable,
      columns: ordersTable.columns.filter(c => c.id !== 'c-orders-user'),
    } as never);
    unsubscribe();

    expect(listener).toHaveBeenCalledWith({
      type: 'ColumnDeleted', tableId: 't-orders', columnId: 'c-orders-user', columnName: 'user_id',
    });
  });

  it('updateTable var olan bir kolonun alanı değiştiğinde "ColumnChanged" yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('ColumnChanged', listener);

    const ordersTable = useSchemaStore.getState().schema!.tables.find(t => t.id === 't-orders')!;
    useSchemaStore.getState().updateTable({
      ...ordersTable,
      columns: ordersTable.columns.map(c => c.id === 'c-orders-user' ? { ...c, isNullable: true } : c),
    } as never);
    unsubscribe();

    expect(listener).toHaveBeenCalledWith({
      type: 'ColumnChanged', tableId: 't-orders', columnId: 'c-orders-user', columnName: 'user_id',
    });
  });

  it('updateTable hiçbir kolon değişmediyse hiçbir kolon olayı yayınlamaz', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('*', listener);

    const ordersTable = useSchemaStore.getState().schema!.tables.find(t => t.id === 't-orders')!;
    useSchemaStore.getState().updateTable({ ...ordersTable, name: 'Orders' } as never); // yalnızca ad değişti
    unsubscribe();

    expect(listener).not.toHaveBeenCalled();
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts -t "updateTable"`
Expected: FAIL — henüz diff/yayın kodu yok.

- [ ] **Step 3: Write minimal implementation**

`updateTable`'ın İÇİNDE, `state` alındıktan hemen sonra (eski tabloya
erişim gerekiyor, `set()`'ten ÖNCE bulunmalı):

```ts
      updateTable: (updatedTable) => {
        const state = get();
        if (!state.schema) return;
        const previousTable = state.schema.tables.find(t => t.id === updatedTable.id);
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        // ... (mevcut newTables/newRelations/newSchema/updatedNode kodu değişmeden) ...

        set({
          schema: newSchema,
          nodes: state.nodes.map(n => n.id === updatedTable.id ? updatedNode : n),
          edges: state.edges.filter(e => survivingRelationIds.has(e.id)),
        });

        if (previousTable) {
          for (const event of diffColumnsForFlowEvents(previousTable, updatedTable)) {
            naminesFlow.emit(event);
          }
        }
      },
```

Dosyanın modül seviyesine (store tanımının dışına, `import`lardan sonra)
yardımcı fonksiyon:

```ts
/**
 * İki tablo sürümü arasındaki kolon farkını Namines Flow olaylarına çevirir.
 *
 * `updateTable` tek çağrıda birden fazla kolonu aynı anda
 * ekleyebilir/silebilir/değiştirebilir (drawer'da toplu kaydetme) — bu
 * yüzden TEK bir olay değil, bir olay LİSTESİ üretiyor.
 */
function diffColumnsForFlowEvents(
  previousTable: SchemaTable,
  updatedTable: SchemaTable
): NaminesFlowEvent[] {
  const events: NaminesFlowEvent[] = [];
  const previousById = new Map(previousTable.columns.map(c => [c.id, c]));
  const updatedById = new Map(updatedTable.columns.map(c => [c.id, c]));

  for (const col of updatedTable.columns) {
    const before = previousById.get(col.id);
    if (!before) {
      events.push({ type: 'ColumnAdded', tableId: updatedTable.id, columnId: col.id, columnName: col.name });
    } else if (JSON.stringify(before) !== JSON.stringify(col)) {
      events.push({ type: 'ColumnChanged', tableId: updatedTable.id, columnId: col.id, columnName: col.name });
    }
  }

  for (const col of previousTable.columns) {
    if (!updatedById.has(col.id)) {
      events.push({ type: 'ColumnDeleted', tableId: updatedTable.id, columnId: col.id, columnName: col.name });
    }
  }

  return events;
}
```

`NaminesFlowEvent` tipini de üstteki import satırına ekle:

```ts
import { naminesFlow, type NaminesFlowEvent } from '../lib/naminesFlowEventBus';
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts -t "updateTable"`
Expected: PASS (4/4)

- [ ] **Step 5: Commit**

```bash
git add frontend/store/useSchemaStore.ts frontend/store/useSchemaStore.test.ts
git commit -m "feat: emit Namines Flow column events from updateTable"
```

---

### Task 4: İlişki olayları — `connectColumns`/`deleteRelation`

**Files:**
- Modify: `frontend/store/useSchemaStore.ts:489-592` (`connectColumns`, `deleteRelation`)
- Test: `frontend/store/useSchemaStore.test.ts` (append)

**Interfaces:**
- Consumes: `naminesFlow.emit` (Task 1).

**Not:** `connectColumns` başarısızlıkta (`{ ok: false, ... }`) erken
`return` ediyor — o yollarda HİÇBİR olay yayınlanmamalı, yalnızca
`{ ok: true, ... }` ile biten başarı yolunda.

- [ ] **Step 1: Write the failing test**

Append to `frontend/store/useSchemaStore.test.ts`:

```ts
  it('connectColumns başarılı bir ilişkide "RelationAdded" yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('RelationAdded', listener);
    const result = useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never);
    unsubscribe();

    expect(result.ok).toBe(true);
    expect(listener).toHaveBeenCalledTimes(1);
    const [event] = listener.mock.calls[0];
    expect(event.type).toBe('RelationAdded');
    expect(event.sourceTableId).toBe('t-orders');
    expect(event.targetTableId).toBe('t-users');
  });

  it('connectColumns başarısız olduğunda hiçbir olay yayınlamaz', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('*', listener);
    const result = useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-orders-id',
    } as never); // target bir PK değil → reddedilir
    unsubscribe();

    expect(result.ok).toBe(false);
    expect(listener).not.toHaveBeenCalled();
  });

  it('deleteRelation "RelationDeleted" yayınlar', async () => {
    const { naminesFlow } = await import('../lib/naminesFlowEventBus');
    useSchemaStore.getState().loadFromSchema(schemaWith() as never);
    useSchemaStore.getState().connectColumns({
      source: 't-orders', target: 't-users', sourceHandle: 'c-orders-user', targetHandle: 'c-users-id',
    } as never);
    const relationId = useSchemaStore.getState().schema!.relations[0].id;

    const listener = vi.fn();
    const unsubscribe = naminesFlow.on('RelationDeleted', listener);
    useSchemaStore.getState().deleteRelation(relationId);
    unsubscribe();

    expect(listener).toHaveBeenCalledWith({
      type: 'RelationDeleted', relationId, sourceTableId: 't-orders', targetTableId: 't-users',
    });
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts -t "RelationAdded\|RelationDeleted\|connectColumns"`
Expected: FAIL — henüz yayın yok.

- [ ] **Step 3: Write minimal implementation**

`connectColumns`'ın BAŞARI dönüşünden hemen önce:

```ts
        set({ schema: newSchema, nodes: finalNodes, edges: newEdges });

        naminesFlow.emit({
          type: 'RelationAdded',
          relationId: newRelation.id,
          sourceTableId: source,
          targetTableId: target,
        });

        return { ok: true, reason: `Created relation ${sourceTable.name}.${sourceColumn.name} → ${targetTable.name}.${targetColumn.name}.` };
```

`deleteRelation`'ın SONUNDA (mevcut `set({...})`'tan hemen sonra):

```ts
        set({
          schema: { ...state.schema, tables: newTables, relations: newRelations },
          nodes: state.nodes.map(n => {
            const table = newTables.find(t => t.id === n.id);
            return table ? { ...n, data: { table } } : n;
          }),
          edges: state.edges.filter(e => e.id !== relationId),
        });

        naminesFlow.emit({
          type: 'RelationDeleted',
          relationId,
          sourceTableId: removed.sourceTableId,
          targetTableId: removed.targetTableId,
        });
      },
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run store/useSchemaStore.test.ts`
Expected: ALL tests in the file PASS (pre-existing + new).

- [ ] **Step 5: Commit**

```bash
git add frontend/store/useSchemaStore.ts frontend/store/useSchemaStore.test.ts
git commit -m "feat: emit Namines Flow relation events from connectColumns/deleteRelation"
```

---

### Task 5: Tam doğrulama

- [ ] **Step 1:** `cd frontend && npx vitest run` — tüm frontend test dosyaları geçmeli.
- [ ] **Step 2:** `cd frontend && npx tsc --noEmit` — temiz.
- [ ] **Step 3:** Bu plan Bölüm 2'nin (canvas `AutomationNode`) doğrudan
  bağımlısı — Task 1'deki `NaminesFlowEvent` tipi ve `naminesFlow.on(...)`
  imzası, Bölüm 2'nin dinleyici bağlayacağı arayüzdür; imza değişmeden
  kalmalı.
