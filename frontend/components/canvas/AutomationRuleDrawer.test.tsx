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

  // I2: AutomationRuleMatcher, RelationAdded/RelationDeleted'ı YALNIZCA proje
  // geneli kurallar (ScopeTableId == null) için eşleştiriyor, ama kural
  // oluşturmanın tek yolu (CanvasContextMenu) her zaman bir tablo id'si
  // veriyor — bu iki seçenek hiçbir kuralda tetiklenemezdi.
  it('does not offer relation triggers that can never fire for a table-scoped rule', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    const triggerSelect = screen.getByLabelText(/trigger/i) as HTMLSelectElement;
    const offered = Array.from(triggerSelect.options).map(o => o.value);

    expect(offered).not.toContain('RelationAdded');
    expect(offered).not.toContain('RelationDeleted');
    // Tablo kapsamlı kuralların gerçekten tetiklenebildiği seçenekler duruyor.
    expect(offered).toEqual(['TableAdded', 'TableDeleted', 'ColumnAdded', 'ColumnDeleted', 'ColumnChanged']);
  });

  it('changing the action to Webhook reveals a URL field, and typing in it saves', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    const urlInput = screen.getByLabelText(/webhook url/i);
    fireEvent.change(urlInput, { target: { value: 'https://example.com/hook' } });
    fireEvent.blur(urlInput);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions[0].actionConfig.url)
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
