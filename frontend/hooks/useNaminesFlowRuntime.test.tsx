// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { renderHook, act, cleanup } from '@testing-library/react';
import { useNaminesFlowRuntime } from './useNaminesFlowRuntime';
import { naminesFlow } from '../lib/naminesFlowEventBus';
import { useAutomationStore, type AutomationRule } from '../store/useAutomationStore';
import { useFlowFiringStore } from '../store/useFlowFiringStore';
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
  triggerType: 'TableDeleted',
  actionType: 'Toast',
  actionConfig: {},
  enabled: true,
  ...over,
});

beforeEach(() => {
  useAutomationStore.setState({ rules: [], selectedRuleId: null });
  useFlowFiringStore.setState({ firings: {} });
  useToastStore.getState().clearAll();
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
    useAutomationStore.setState({ rules: [rule({ actionType: 'Webhook' })] });
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
