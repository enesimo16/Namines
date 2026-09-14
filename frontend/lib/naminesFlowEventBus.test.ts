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
