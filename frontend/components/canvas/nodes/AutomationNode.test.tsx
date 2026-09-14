// @vitest-environment jsdom
import { describe, it, expect, afterEach, beforeEach } from 'vitest';
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
