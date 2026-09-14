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
