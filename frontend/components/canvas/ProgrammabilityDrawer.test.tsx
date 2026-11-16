// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import ProgrammabilityDrawer from './ProgrammabilityDrawer';
import { useSchemaStore } from '../../store/useSchemaStore';
import { schemaService } from '../../services/api';

vi.mock('../../hooks/useAIGateway', () => ({
  useAIGateway: () => ({ checkAccess: () => true }),
}));

describe('ProgrammabilityDrawer', () => {
  beforeEach(() => {
    useSchemaStore.setState({
      schema: {
        schemaId: 's1',
        name: 'shop',
        tables: [
          {
            id: 't_orders',
            name: 'orders',
            columns: [{ id: 'c_id', name: 'id', type: 'UUID', length: null, isPK: true, isFK: false, isNullable: false, defaultValue: null }],
          },
        ],
        relations: [],
        triggers: [],
        storedProcedures: [],
        enums: [],
      },
      dbType: 'PostgreSQL',
      naiModel: 'nai',
    } as never);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('opens on custom event and synthesizes triggers/procedures via AI prompt', async () => {
    const spy = vi.spyOn(schemaService, 'reviseSchema').mockResolvedValue({
      schemaId: 's1',
      name: 'shop',
      tables: [],
      relations: [],
      triggers: [
        {
          id: 'trg_orders_stock',
          tableId: 't_orders',
          timing: 'After',
          event: 'Insert',
          targetEngine: 'PostgreSQL',
          body: 'CREATE TRIGGER trg_orders_stock ...',
        },
      ],
      storedProcedures: [
        {
          id: 'sp_monthly_revenue',
          name: 'sp_monthly_revenue',
          targetEngine: 'PostgreSQL',
          parameters: [{ name: 'p_year', type: 'INT' }],
          body: 'CREATE PROCEDURE sp_monthly_revenue(p_year INT) ...',
        },
      ],
      enums: [],
    });

    render(<ProgrammabilityDrawer />);

    window.dispatchEvent(new CustomEvent('namines:open-programmability'));

    await waitFor(() => {
      expect(screen.getByText('Routines, Triggers & Enums')).toBeInTheDocument();
    });

    const aiInput = screen.getByLabelText(/AI Routine & Trigger Synthesis/i);
    fireEvent.change(aiInput, { target: { value: 'Create sp_monthly_revenue and trigger on orders' } });
    fireEvent.click(screen.getByRole('button', { name: /Generate/i }));

    await waitFor(() => {
      expect(spy).toHaveBeenCalledTimes(1);
      expect(useSchemaStore.getState().schema?.storedProcedures).toHaveLength(1);
      expect(useSchemaStore.getState().schema?.triggers).toHaveLength(1);
    });

    expect(screen.getByDisplayValue('sp_monthly_revenue')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Triggers \(1\)/i }));
    expect(screen.getByDisplayValue('trg_orders_stock')).toBeInTheDocument();
  });
});
