import { Handle, Position, NodeProps, Node } from '@xyflow/react';
import { Zap, PauseCircle } from 'lucide-react';
import { useAutomationStore } from '../../../store/useAutomationStore';
import { useFlowFiringStore } from '../../../store/useFlowFiringStore';

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

const ACTION_LABEL: Record<string, string> = {
  Toast: 'Notify',
  Webhook: 'Webhook',
  DbaCheck: 'DBA check',
  SeedData: 'Sample data',
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
  const isFiring = useFlowFiringStore(s => data.ruleId in s.firings);

  if (!rule) return null;

  // Devre dışı kural soluklaşıyor — `enabled` modelde hep vardı ama hiçbir
  // yerde görünmüyordu, yani kapalı bir kural açık olandan ayırt edilemiyordu.
  const disabled = !rule.enabled;

  return (
    <div
      className={`rounded-[var(--radius-card)] border-2 bg-warning/10 backdrop-blur-sm px-3 py-2 min-w-[190px] cursor-pointer transition-all duration-150 hover:border-warning hover:bg-warning/20 ${
        selected ? 'border-warning' : 'border-warning/50'
      } ${isFiring ? 'scale-105 border-warning bg-warning/30 shadow-[0_0_20px_color-mix(in_srgb,var(--color-warning)_45%,transparent)]' : ''} ${
        disabled ? 'opacity-50' : ''
      }`}
    >
      <Handle type="target" position={Position.Left} className="!bg-warning" />
      <button
        type="button"
        aria-label="Namines Flow — edit this rule"
        onClick={() => setSelectedRuleId(rule.id)}
        className="flex items-center gap-1.5 w-full text-left cursor-pointer"
      >
        <Zap className={`w-3.5 h-3.5 text-warning-text shrink-0 ${isFiring ? 'animate-pulse' : ''}`} />
        <span className="text-xs font-semibold text-warning-text">Namines Flow</span>
        {disabled && <PauseCircle className="w-3 h-3 text-content-muted shrink-0 ml-auto" />}
      </button>
      <div className="mt-1 flex items-center gap-1 text-[11px] text-content-secondary">
        <span>{TRIGGER_LABEL[rule.triggerType] ?? rule.triggerType}</span>
        <span aria-hidden="true">→</span>
        <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-warning/20 text-warning-text font-medium">
          {ACTION_LABEL[rule.actionType] ?? rule.actionType}
        </span>
      </div>
    </div>
  );
}

export default AutomationNode;
