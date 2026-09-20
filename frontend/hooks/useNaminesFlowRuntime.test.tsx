// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { renderHook, act, cleanup } from '@testing-library/react';
import { useNaminesFlowRuntime } from './useNaminesFlowRuntime';
import { naminesFlow } from '../lib/naminesFlowEventBus';
import { useAutomationStore, type AutomationRule } from '../store/useAutomationStore';
import { useFlowBarStore } from '../store/useFlowBarStore';
import { useFlowFiringStore } from '../store/useFlowFiringStore';
import { useSchemaStore } from '../store/useSchemaStore';
import { useToastStore } from '../store/useToastStore';

/**
 * Bu test GERÇEK olay veriyolunu ve GERÇEK store'ları kullanıyor — hiçbiri
 * mock'lanmıyor. Amaç tam olarak bu zincirin kopuk olduğunu yakalamak:
 * `useSchemaStore` olay yayınlıyordu, `naminesFlow.on(...)` ise üretim
 * kodunda hiç çağrılmadığı için `Toast` aksiyonu hiçbir şey yapmıyordu.
 */

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

beforeEach(() => {
  useAutomationStore.setState({ rules: [], selectedRuleId: null });
  useFlowFiringStore.setState({ firings: {} });
  useFlowBarStore.setState({ paused: false });
  useToastStore.getState().clearAll();
  useSchemaStore.setState({ schema: null } as never);
});

// Bu projede RTL'in otomatik temizligi kurulu DEGIL (vitest `globals: false`).
// Acik `cleanup` olmadan her testin hook'u monte kalir ve olay veriyoluna
// abone olmaya devam eder — sonraki testler baskasinin dinleyicisini gorur.
afterEach(cleanup);

describe('useNaminesFlowRuntime', () => {
  it('eslesen Toast kurali icin toast gosterir ve node u isaretler', () => {
    useAutomationStore.setState({ rules: [rule()] });
    renderHook(() => useNaminesFlowRuntime());

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts).toHaveLength(1);
    expect(useToastStore.getState().toasts[0].message).toContain('Orders');
    expect(useFlowFiringStore.getState().firings).toHaveProperty('r1');
  });

  it('Toast disi aksiyonda toast basmaz ama node yine de isaretlenir', () => {
    // Webhook sunucuda calisir; istemcide gorunen tek sey "bu kural tetiklendi"
    // animasyonu olmali — sessizlik degil.
    useAutomationStore.setState({ rules: [rule({ actions: [{ actionType: 'Webhook', actionConfig: {} }] })] });
    renderHook(() => useNaminesFlowRuntime());

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts).toHaveLength(0);
    expect(useFlowFiringStore.getState().firings).toHaveProperty('r1');
  });

  it('eslesmeyen olayda hicbir sey yapmaz', () => {
    useAutomationStore.setState({ rules: [rule()] });
    renderHook(() => useNaminesFlowRuntime());

    act(() => {
      naminesFlow.emit({ type: 'TableAdded', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts).toHaveLength(0);
    expect(useFlowFiringStore.getState().firings).toEqual({});
  });

  it('abonelik kurulduktan SONRA eklenen kurali da gorur', () => {
    // Dinleyici store a abone olmak yerine `getState()` okuyor; bu test o
    // kararin dogru calistigini koruyor — aksi hâlde hook mount olduktan
    // sonra olusturulan kurallar hic tetiklenmezdi.
    renderHook(() => useNaminesFlowRuntime());
    useAutomationStore.setState({ rules: [rule()] });

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts).toHaveLength(1);
  });

  it('flow bar duraklatilmisken hicbir sey tetiklenmez', () => {
    useAutomationStore.setState({ rules: [rule()] });
    useFlowBarStore.setState({ paused: true });
    renderHook(() => useNaminesFlowRuntime());

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts).toHaveLength(0);
    expect(useFlowFiringStore.getState().firings).toEqual({});
  });

  it('kullanici mesaji sablon degiskenleriyle doldurularak gosteriliyor', () => {
    useSchemaStore.setState({
      schema: { schemaId: 's1', name: 'Shop', tables: [{ id: 't1', name: 'orders', columns: [] }], relations: [] },
    } as never);
    useAutomationStore.setState({
      rules: [rule({ actions: [{ actionType: 'Toast', actionConfig: { message: '{{projectName}}: {{tableName}} gitti' } }] })],
    });
    renderHook(() => useNaminesFlowRuntime());

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'orders' });
    });

    expect(useToastStore.getState().toasts[0].message).toBe('Shop: orders gitti');
  });

  it('mesaj bos birakilirsa varsayilan metne dusuyor', () => {
    useAutomationStore.setState({
      rules: [rule({ actions: [{ actionType: 'Toast', actionConfig: { message: '   ' } }] })],
    });
    renderHook(() => useNaminesFlowRuntime());

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts[0].message).toContain('Orders');
  });

  it('unmount sonrasi artik dinlemez', () => {
    useAutomationStore.setState({ rules: [rule()] });
    const { unmount } = renderHook(() => useNaminesFlowRuntime());
    unmount();

    act(() => {
      naminesFlow.emit({ type: 'TableDeleted', tableId: 't1', tableName: 'Orders' });
    });

    expect(useToastStore.getState().toasts).toHaveLength(0);
  });
});
