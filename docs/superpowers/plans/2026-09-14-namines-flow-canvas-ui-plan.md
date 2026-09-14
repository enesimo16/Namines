# Namines Flow — Canvas UI (Spec Bölüm 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Canvas üzerinde, bir tabloya bağlı, görsel ve **açıkça "Namines
Flow" markalı** bir otomasyon node'u — kullanıcı sağ tıkla ekliyor,
tıklayınca tetikleyici+aksiyon seçtiği bir çekmece açılıyor.

**Architecture:** Yeni bir React Flow node tipi (`automationNode`) ve onu
besleyen yeni, izole bir Zustand store (`useAutomationStore`) — bu store
şimdilik yalnızca istemci tarafında (localStorage, mevcut `persist`
deseniyle) tutuluyor; Bölüm 3, bu store'un CRUD fonksiyonlarının İÇİNİ
gerçek backend çağrılarıyla değiştirecek, dışa açık arayüzü (fonksiyon
imzaları) DEĞİŞMEYECEK — bu yüzden Bölüm 2 backend'i beklemeden tek
başına çalışan, test edilebilir bir teslim.

**Tech Stack:** React, Zustand, `@xyflow/react`, `@radix-ui/react-dialog`,
`@radix-ui/react-context-menu`, Vitest, React Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-14-namines-flow-design.md`
(Bölüm 2)

## Global Constraints

- **İsimlendirme SOYUT olamaz.** Kullanıcıya görünen HER metin "Namines
  Flow" markasını taşımalı: context menu öğesi "Add to Namines Flow"
  (jenerik "Add automation" DEĞİL), node üzerindeki başlık "Namines Flow",
  çekmece başlığı "Namines Flow — <tablo adı>". Kullanıcının açık talebi:
  "bu sistemin namines flow adında olduğunu göstereceğiz soyutluk yok".
- Node/edge tipleri `frontend/app/canvas/page.tsx:335-336`'daki
  `nodeTypes`/`edgeTypes` haritalarına eklenir — mevcut `tableNode`/
  `relationEdge` girdileri DEĞİŞMEZ.
- Renk: mevcut `--color-warning`/`text-warning`/`border-warning` token'ları
  kullanılır — yeni bir renk token'ı İCAT EDİLMEZ (bkz. `globals.css:456-458`).
- `Task 1`'deki store arayüzü, Bölüm 3'ün üzerine backend entegrasyonu
  ekleyeceği temel — fonksiyon imzaları (`addRule`, `updateRule`,
  `deleteRule`, `rulesForTable`) bu planda sabitlenip Bölüm 3'te
  DEĞİŞTİRİLMEMELİ.

---

### Task 1: `useAutomationStore` — kural deposu

**Files:**
- Create: `frontend/store/useAutomationStore.ts`
- Test: `frontend/store/useAutomationStore.test.ts`

**Interfaces:**
- Consumes: `NaminesFlowEvent['type']` (`frontend/lib/naminesFlowEventBus.ts`, Bölüm 1).
- Produces:
  ```ts
  export type AutomationActionType = 'Webhook' | 'DbaCheck' | 'SeedData' | 'Toast';

  export interface AutomationRule {
    id: string;
    scopeTableId: string;
    triggerType: NaminesFlowEvent['type'];
    actionType: AutomationActionType;
    actionConfig: { url?: string };
    enabled: boolean;
  }

  interface AutomationStoreState {
    rules: AutomationRule[];
    selectedRuleId: string | null;
    addRule: (scopeTableId: string, triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType) => string;
    updateRule: (id: string, patch: Partial<Pick<AutomationRule, 'triggerType' | 'actionType' | 'actionConfig' | 'enabled'>>) => void;
    deleteRule: (id: string) => void;
    deleteRulesForTable: (tableId: string) => void;
    rulesForTable: (tableId: string) => AutomationRule[];
    setSelectedRuleId: (id: string | null) => void;
  }

  export const useAutomationStore: UseBoundStore<StoreApi<AutomationStoreState>>;
  ```

- [ ] **Step 1: Write the failing test**

```ts
import { beforeEach, describe, expect, it } from 'vitest';
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run store/useAutomationStore.test.ts`
Expected: FAIL — `useAutomationStore.ts` does not exist.

- [ ] **Step 3: Write minimal implementation**

```ts
import { create } from 'zustand';
import type { NaminesFlowEvent } from '../lib/naminesFlowEventBus';

const genId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

export type AutomationActionType = 'Webhook' | 'DbaCheck' | 'SeedData' | 'Toast';

export interface AutomationRule {
  id: string;
  scopeTableId: string;
  triggerType: NaminesFlowEvent['type'];
  actionType: AutomationActionType;
  actionConfig: { url?: string };
  enabled: boolean;
}

interface AutomationStoreState {
  rules: AutomationRule[];
  selectedRuleId: string | null;
  addRule: (scopeTableId: string, triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType) => string;
  updateRule: (id: string, patch: Partial<Pick<AutomationRule, 'triggerType' | 'actionType' | 'actionConfig' | 'enabled'>>) => void;
  deleteRule: (id: string) => void;
  deleteRulesForTable: (tableId: string) => void;
  rulesForTable: (tableId: string) => AutomationRule[];
  setSelectedRuleId: (id: string | null) => void;
}

/**
 * Namines Flow kurallarının istemci tarafı deposu.
 *
 * Bölüm 3'e kadar YALNIZCA istemcide (localStorage) yaşar — sunucu tarafı
 * aksiyonlar (webhook/DBA/seed) henüz bu kuralları görmüyor, yalnızca
 * `Toast` aksiyonu bugünden itibaren çalışabilir (bkz. AutomationNode).
 * Bölüm 3, bu dosyanın action gövdelerini gerçek `/api/automation/rules`
 * çağrılarıyla değiştirecek — dışa açık imzalar SABİT kalıyor ki
 * `AutomationNode`/`AutomationRuleDrawer` hiç değişmesin.
 */
export const useAutomationStore = create<AutomationStoreState>((set, get) => ({
  rules: [],
  selectedRuleId: null,

  addRule: (scopeTableId, triggerType, actionType) => {
    const id = genId();
    set(state => ({
      rules: [...state.rules, { id, scopeTableId, triggerType, actionType, actionConfig: {}, enabled: true }],
    }));
    return id;
  },

  updateRule: (id, patch) => {
    set(state => ({
      rules: state.rules.map(r => r.id === id ? { ...r, ...patch } : r),
    }));
  },

  deleteRule: (id) => {
    set(state => ({
      rules: state.rules.filter(r => r.id !== id),
      selectedRuleId: state.selectedRuleId === id ? null : state.selectedRuleId,
    }));
  },

  deleteRulesForTable: (tableId) => {
    set(state => ({ rules: state.rules.filter(r => r.scopeTableId !== tableId) }));
  },

  rulesForTable: (tableId) => get().rules.filter(r => r.scopeTableId === tableId),

  setSelectedRuleId: (id) => set({ selectedRuleId: id }),
}));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run store/useAutomationStore.test.ts`
Expected: PASS (6/6)

- [ ] **Step 5: Commit**

```bash
git add frontend/store/useAutomationStore.ts frontend/store/useAutomationStore.test.ts
git commit -m "feat: add Namines Flow automation rule store"
```

---

### Task 2: `AutomationNode` — canvas görseli

**Files:**
- Create: `frontend/components/canvas/nodes/AutomationNode.tsx`
- Test: `frontend/components/canvas/nodes/AutomationNode.test.tsx`

**Interfaces:**
- Consumes: `useAutomationStore` (Task 1), `AutomationRule`,
  `AutomationActionType`.
- Produces: `AutomationNodeType = Node<{ ruleId: string }, 'automationNode'>`
  — Task 4'te `nodeTypes` haritasına bu isimle eklenecek.

- [ ] **Step 1: Write the failing test**

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import { ReactFlowProvider } from '@xyflow/react';
import AutomationNode from './AutomationNode';
import { useAutomationStore } from '../../../store/useAutomationStore';

function renderNode(ruleId: string) {
  return render(
    <ReactFlowProvider>
      <AutomationNode
        id="a1"
        data={{ ruleId }}
        selected={false}
        type="automationNode"
        dragging={false}
        zIndex={0}
        isConnectable
        xPos={0}
        yPos={0}
      />
    </ReactFlowProvider>
  );
}

describe('AutomationNode', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
  });
  afterEach(() => cleanup());

  it('shows the Namines Flow brand name, never a generic label', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    renderNode(id);

    expect(screen.getByText('Namines Flow')).toBeInTheDocument();
  });

  it('summarizes the trigger and action', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    renderNode(id);

    expect(screen.getByText(/On Delete/i)).toBeInTheDocument();
    expect(screen.getByText(/Webhook/i)).toBeInTheDocument();
  });

  it('clicking the node selects its rule for editing', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'ColumnAdded', 'Toast');
    renderNode(id);

    fireEvent.click(screen.getByRole('button', { name: /namines flow/i }));

    expect(useAutomationStore.getState().selectedRuleId).toBe(id);
  });

  it('renders nothing meaningful if the rule was deleted out from under it', () => {
    // Node silinmeden ÖNCE rule başka bir yerden silinmiş olabilir (yarış) —
    // çökmemeli.
    renderNode('does-not-exist');
    expect(screen.queryByText('Namines Flow')).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run components/canvas/nodes/AutomationNode.test.tsx`
Expected: FAIL — `AutomationNode.tsx` does not exist.

- [ ] **Step 3: Write minimal implementation**

```tsx
import { Handle, Position, NodeProps, Node } from '@xyflow/react';
import { Zap } from 'lucide-react';
import { useAutomationStore } from '../../../store/useAutomationStore';

export type AutomationNodeType = Node<{ ruleId: string }, 'automationNode'>;

const TRIGGER_LABEL: Record<string, string> = {
  TableAdded: 'On Table Added',
  TableDeleted: 'On Delete',
  ColumnAdded: 'On Column Added',
  ColumnDeleted: 'On Column Deleted',
  ColumnChanged: 'On Column Changed',
  RelationAdded: 'On Relation Added',
  RelationDeleted: 'On Relation Deleted',
};

/**
 * Namines Flow'un canvas üzerindeki görsel temsili.
 *
 * İsim jenerik "Automation" DEĞİL, her zaman "Namines Flow" — kullanıcının
 * açık talebi bu sistemin adının canvas'ta görünür olması, soyut bir
 * "otomasyon kutucuğu" değil.
 */
function AutomationNode({ data, selected }: NodeProps<AutomationNodeType>) {
  const rule = useAutomationStore(s => s.rules.find(r => r.id === data.ruleId));
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);

  if (!rule) return null;

  return (
    <div
      className={`rounded-[var(--radius-card)] border-2 bg-warning/10 backdrop-blur-sm px-3 py-2 min-w-[180px] transition-colors ${
        selected ? 'border-warning' : 'border-warning/50'
      }`}
    >
      <Handle type="target" position={Position.Left} className="!bg-warning" />
      <button
        type="button"
        aria-label="Namines Flow — edit this rule"
        onClick={() => setSelectedRuleId(rule.id)}
        className="flex items-center gap-1.5 w-full text-left"
      >
        <Zap className="w-3.5 h-3.5 text-warning-text shrink-0" />
        <span className="text-xs font-semibold text-warning-text">Namines Flow</span>
      </button>
      <div className="mt-1 text-[11px] text-content-secondary">
        {TRIGGER_LABEL[rule.triggerType] ?? rule.triggerType} → {rule.actionType}
      </div>
    </div>
  );
}

export default AutomationNode;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run components/canvas/nodes/AutomationNode.test.tsx`
Expected: PASS (4/4)

- [ ] **Step 5: Commit**

```bash
git add frontend/components/canvas/nodes/AutomationNode.tsx frontend/components/canvas/nodes/AutomationNode.test.tsx
git commit -m "feat: add the Namines Flow canvas node"
```

---

### Task 3: `AutomationRuleDrawer` — yapılandırma çekmecesi

**Files:**
- Create: `frontend/components/canvas/AutomationRuleDrawer.tsx`
- Test: `frontend/components/canvas/AutomationRuleDrawer.test.tsx`

**Interfaces:**
- Consumes: `useAutomationStore` (Task 1) — `selectedRuleId`,
  `setSelectedRuleId`, `updateRule`, `rules`.

- [ ] **Step 1: Write the failing test**

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach, beforeEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import AutomationRuleDrawer from './AutomationRuleDrawer';
import { useAutomationStore } from '../../store/useAutomationStore';

describe('AutomationRuleDrawer', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
  });
  afterEach(() => cleanup());

  it('renders nothing when no rule is selected', () => {
    render(<AutomationRuleDrawer />);
    expect(screen.queryByText(/Namines Flow —/)).not.toBeInTheDocument();
  });

  it('shows the Namines Flow brand in its title, with the trigger scope', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    expect(screen.getByText(/Namines Flow/)).toBeInTheDocument();
  });

  it('changing the action to Webhook reveals a URL field, and typing in it saves', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    const urlInput = screen.getByLabelText(/webhook url/i);
    fireEvent.change(urlInput, { target: { value: 'https://example.com/hook' } });
    fireEvent.blur(urlInput);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actionConfig.url)
      .toBe('https://example.com/hook');
  });

  it('switching the action away from Webhook hides the URL field', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    expect(screen.queryByLabelText(/webhook url/i)).not.toBeInTheDocument();
  });

  it('the close button clears the selection without deleting the rule', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);
    fireEvent.click(screen.getByRole('button', { name: /close/i }));

    expect(useAutomationStore.getState().selectedRuleId).toBeNull();
    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run components/canvas/AutomationRuleDrawer.test.tsx`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Write minimal implementation**

```tsx
'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { X, Zap } from 'lucide-react';
import { useAutomationStore, type AutomationActionType } from '../../store/useAutomationStore';
import type { NaminesFlowEvent } from '../../lib/naminesFlowEventBus';

const TRIGGER_OPTIONS: { value: NaminesFlowEvent['type']; label: string }[] = [
  { value: 'TableAdded', label: 'Table added' },
  { value: 'TableDeleted', label: 'Table deleted' },
  { value: 'ColumnAdded', label: 'Column added' },
  { value: 'ColumnDeleted', label: 'Column deleted' },
  { value: 'ColumnChanged', label: 'Column changed' },
  { value: 'RelationAdded', label: 'Relation added' },
  { value: 'RelationDeleted', label: 'Relation deleted' },
];

const ACTION_OPTIONS: { value: AutomationActionType; label: string }[] = [
  { value: 'Toast', label: 'Show a notification' },
  { value: 'Webhook', label: 'Call a webhook' },
  { value: 'DbaCheck', label: 'Run a DBA check' },
  { value: 'SeedData', label: 'Generate sample data' },
];

const inputClass = 'w-full bg-surface-700 border border-content-primary/10 rounded-[var(--radius-control)] px-3 py-2 text-sm text-content-primary placeholder:text-content-subtle focus:outline-none focus:border-focus-ring transition-colors';

/**
 * Namines Flow kural çekmecesi — bir tabloya bağlı bir otomasyon kuralının
 * tetikleyicisini ve aksiyonunu düzenler.
 */
export default function AutomationRuleDrawer() {
  const selectedRuleId = useAutomationStore(s => s.selectedRuleId);
  const rule = useAutomationStore(s => s.rules.find(r => r.id === s.selectedRuleId));
  const updateRule = useAutomationStore(s => s.updateRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);

  const isOpen = !!selectedRuleId && !!rule;

  return (
    <Dialog.Root open={isOpen} onOpenChange={(open) => { if (!open) setSelectedRuleId(null); }}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-scrim/60 z-[90]" />
        <Dialog.Content className="fixed right-0 top-0 h-full w-full max-w-sm bg-surface-800 border-l border-content-primary/10 z-[91] p-5 flex flex-col gap-4 overflow-y-auto">
          <div className="flex items-center justify-between">
            <Dialog.Title className="flex items-center gap-2 text-sm font-semibold text-content-primary">
              <Zap className="w-4 h-4 text-warning-text" />
              Namines Flow
            </Dialog.Title>
            <Dialog.Close asChild>
              <button type="button" aria-label="Close" className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary">
                <X className="w-4 h-4" />
              </button>
            </Dialog.Close>
          </div>

          {rule && (
            <>
              <label className="flex flex-col gap-1.5 text-xs font-medium text-content-secondary">
                Trigger
                <select
                  className={inputClass}
                  value={rule.triggerType}
                  onChange={(e) => updateRule(rule.id, { triggerType: e.target.value as NaminesFlowEvent['type'] })}
                >
                  {TRIGGER_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </label>

              <label className="flex flex-col gap-1.5 text-xs font-medium text-content-secondary">
                Action
                <select
                  className={inputClass}
                  value={rule.actionType}
                  onChange={(e) => updateRule(rule.id, { actionType: e.target.value as AutomationActionType })}
                >
                  {ACTION_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </label>

              {rule.actionType === 'Webhook' && (
                <label className="flex flex-col gap-1.5 text-xs font-medium text-content-secondary">
                  Webhook URL
                  <input
                    type="url"
                    aria-label="Webhook URL"
                    className={inputClass}
                    defaultValue={rule.actionConfig.url ?? ''}
                    placeholder="https://example.com/hook"
                    onBlur={(e) => updateRule(rule.id, { actionConfig: { ...rule.actionConfig, url: e.target.value } })}
                  />
                </label>
              )}
            </>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run components/canvas/AutomationRuleDrawer.test.tsx`
Expected: PASS (5/5)

- [ ] **Step 5: Commit**

```bash
git add frontend/components/canvas/AutomationRuleDrawer.tsx frontend/components/canvas/AutomationRuleDrawer.test.tsx
git commit -m "feat: add the Namines Flow rule drawer"
```

---

### Task 4: Canvas kablolama — context menu, node tipi, silme

**Files:**
- Modify: `frontend/components/canvas/CanvasContextMenu.tsx`
- Modify: `frontend/app/canvas/page.tsx:335-336` (`nodeTypes`), plus mount points
- Test: manual browser verification (Step 6) — bu görev React Flow'un
  gerçek DOM ölçümüne (node yerleşimi) bağımlı olduğu için birim testi
  yerine tarayıcıda doğrulanıyor; Task 1-3'ün birim testleri mantığı
  zaten kilitliyor.

**Interfaces:**
- Consumes: `useAutomationStore.addRule`/`deleteRulesForTable` (Task 1),
  `AutomationNode`/`AutomationNodeType` (Task 2), `AutomationRuleDrawer`
  (Task 3).

- [ ] **Step 1: Add the context menu item**

`CanvasContextMenu.tsx` — import ekle:

```tsx
import { Plus, Trash2, Pencil, Table2, Copy, Zap } from 'lucide-react';
import { useAutomationStore } from '../../store/useAutomationStore';
```

Component içine, `useSchemaStore` çağrısının yanına:

```tsx
  const addAutomationRule = useAutomationStore(s => s.addRule);
```

`handleAddTable`'ın yanına yeni bir handler:

```tsx
  /**
   * Namines Flow node'unu seçilen tabloya bağlı olarak ekler. Node'un
   * kendisi React Flow state'inde YAŞAMAZ — AutomationNode, rule listesinden
   * TÜRETİLİR (bkz. Task 4 Step 2), bu yüzden burada yalnızca kural
   * oluşturuluyor.
   */
  const handleAddAutomation = () => {
    if (!menuState?.nodeId) return;
    addAutomationRule(menuState.nodeId, 'TableDeleted', 'Toast');
    setMenuState(null);
  };
```

"Table Operations" bölümündeki `ContextMenu.Separator`'dan hemen önce
yeni bir öğe:

```tsx
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-warning/20 hover:text-warning-text focus:bg-warning/20 focus:text-warning-text"
                  onSelect={handleAddAutomation}
                >
                  <Zap className="w-4 h-4 text-warning-text" />
                  <span>Add to Namines Flow</span>
                </ContextMenu.Item>
```

- [ ] **Step 2: Derive automation nodes/edges from the rule store in `canvas/page.tsx`**

`nodeTypes`/`edgeTypes`'ın tanımlandığı yere (satır ~335):

```tsx
  const nodeTypes = useMemo(() => ({ tableNode: TableNode, automationNode: AutomationNode }), []);
  const edgeTypes = useMemo(() => ({ relationEdge: RelationEdge }), []);
```

Import ekle:

```tsx
import AutomationNode from '../../components/canvas/nodes/AutomationNode';
import AutomationRuleDrawer from '../../components/canvas/AutomationRuleDrawer';
import { useAutomationStore } from '../../store/useAutomationStore';
```

`ReactFlow`'a geçirilen `nodes`/`edges`'in TÜREV bir versiyonu
hazırlanır — mevcut `nodes`/`edges` state'ine dokunulmadan, render
sırasında otomasyon node/edge'leri eklenir:

```tsx
  const automationRules = useAutomationStore(s => s.rules);

  const nodesWithAutomation = useMemo(() => {
    const automationNodes = automationRules.map((rule, i) => {
      const anchor = nodes.find(n => n.id === rule.scopeTableId);
      const anchorX = anchor?.position.x ?? 0;
      const anchorY = anchor?.position.y ?? 0;
      return {
        id: `automation-${rule.id}`,
        type: 'automationNode',
        position: { x: anchorX + 320, y: anchorY + i * 70 },
        data: { ruleId: rule.id },
      };
    });
    return [...nodes, ...automationNodes];
  }, [nodes, automationRules]);

  const edgesWithAutomation = useMemo(() => {
    const automationEdges = automationRules.map(rule => ({
      id: `automation-edge-${rule.id}`,
      source: rule.scopeTableId,
      target: `automation-${rule.id}`,
      style: { strokeDasharray: '4 4', stroke: 'var(--color-warning-text)' },
      animated: false,
    }));
    return [...edges, ...automationEdges];
  }, [edges, automationRules]);
```

`<ReactFlow nodes={nodes} edges={edges} .../>` çağrısındaki `nodes`/`edges`
prop'ları `nodesWithAutomation`/`edgesWithAutomation` ile değiştirilir.
`<TableEditorDrawer />`'ın yanına `<AutomationRuleDrawer />` eklenir.

- [ ] **Step 3: Cascade rule deletion when a table is deleted**

`useSchemaStore.deleteTable`, Namines Flow kurallarını BİLMEMELİ (store'lar
birbirinden bağımsız kalmalı — bkz. spec'in "şemadan ayrı depolama"
kararı). Bunun yerine `CanvasContextMenu.tsx`'teki `handleDeleteTable`'a
eklenir:

```tsx
  const deleteRulesForTable = useAutomationStore(s => s.deleteRulesForTable);

  const handleDeleteTable = () => {
    if (!menuState?.nodeId) return;
    deleteRulesForTable(menuState.nodeId);
    deleteTable(menuState.nodeId);
    setMenuState(null);
  };
```

Aynı temizlik, `TableNode.tsx`'in kendi Delete tuşu/menü yolunda da
gerekiyorsa (klavye ile silme) oraya da eklenmeli — bu adımı uygularken
`deleteTable`'ın TÜM çağrı noktalarını (`grep -rn "deleteTable(" frontend`)
tara ve her birine aynı `deleteRulesForTable` çağrısını ekle.

- [ ] **Step 4: Verify the frontend compiles**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 5: Run the full test suite**

Run: `cd frontend && npx vitest run`
Expected: all files PASS (Task 1-3's new tests + all pre-existing).

- [ ] **Step 6: Verify in the browser**

Start the dev server, open `/canvas` with a loaded schema:
1. Right-click a table → confirm "Add to Namines Flow" appears in the
   context menu (with the ⚡ icon, warning/amber color).
2. Click it → confirm a new node appears connected to the table with a
   dashed amber edge, labeled "Namines Flow" with a trigger→action
   summary.
3. Click the node → confirm `AutomationRuleDrawer` opens, titled
   "Namines Flow", with the correct trigger/action preselected.
4. Change the action to "Call a webhook" → confirm a URL field appears;
   type a URL, click elsewhere → confirm the node's summary text updates
   to reflect the new action.
5. Delete the anchor table → confirm the Namines Flow node and its edge
   disappear too (no orphaned node).

- [ ] **Step 7: Commit**

```bash
git add frontend/components/canvas/CanvasContextMenu.tsx frontend/app/canvas/page.tsx
git commit -m "feat: wire Namines Flow nodes into the canvas"
```

---

### Task 5: Tam doğrulama

- [ ] **Step 1:** `cd frontend && npx vitest run` — tümü geçmeli.
- [ ] **Step 2:** `cd frontend && npx tsc --noEmit` — temiz.
- [ ] **Step 3:** Task 4 Step 6'daki tarayıcı doğrulamasının ekran
  görüntüsünü kullanıcıya gönder.
