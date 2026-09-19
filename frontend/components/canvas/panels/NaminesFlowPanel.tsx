'use client';

import { useReactFlow } from '@xyflow/react';
import { Crosshair, Pencil, Trash2, X, Zap } from 'lucide-react';
import { useFlowBarStore } from '../../../store/useFlowBarStore';
import { useAutomationStore } from '../../../store/useAutomationStore';
import { useFlowNodePositionStore } from '../../../store/useFlowNodePositionStore';
import { useSchemaStore } from '../../../store/useSchemaStore';

const TRIGGER_LABEL: Record<string, string> = {
  TableAdded: 'Table added',
  TableDeleted: 'Table deleted',
  ColumnAdded: 'Column added',
  ColumnDeleted: 'Column deleted',
  ColumnChanged: 'Column changed',
  RelationAdded: 'Relation added',
  RelationDeleted: 'Relation deleted',
};

const ACTION_LABEL: Record<string, string> = {
  Toast: 'Notify',
  Webhook: 'Webhook',
  DbaCheck: 'DBA check',
  SeedData: 'Sample data',
};

/**
 * Projedeki tüm Namines Flow kurallarının listesi.
 *
 * Kuralları yalnızca canvas üzerinden görmek yetmiyordu: düğümler ankor
 * tabloya göre sabit bir ofsete düşüyor, üst üste binebiliyor ve ekran
 * dışında kalabiliyorlar. Buradaki "odaklan" düğmesi canvas'ı kuralın
 * üstüne götürüyor; `enabled` anahtarı ise modelde baştan beri var olduğu
 * hâlde hiçbir arayüzde açılmamıştı.
 */
export default function NaminesFlowPanel() {
  const { setCenter, getNode, getZoom } = useReactFlow();

  const panelOpen = useFlowBarStore(s => s.panelOpen);
  const setPanelOpen = useFlowBarStore(s => s.setPanelOpen);
  const paused = useFlowBarStore(s => s.paused);

  const rules = useAutomationStore(s => s.rules);
  const updateRule = useAutomationStore(s => s.updateRule);
  const deleteRule = useAutomationStore(s => s.deleteRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);
  const clearFlowNodePosition = useFlowNodePositionStore(s => s.clearPosition);

  const tables = useSchemaStore(s => s.schema?.tables);

  if (!panelOpen) return null;

  const tableName = (tableId: string) =>
    tables?.find(t => t.id === tableId)?.name ?? 'unknown table';

  const focusRule = (ruleId: string) => {
    const node = getNode(`automation-${ruleId}`);
    if (!node) return;
    const width = node.measured?.width ?? 190;
    const height = node.measured?.height ?? 58;
    // Animasyonsuz (duration verilmiyor): animasyonlu kaydırma
    // requestAnimationFrame'e bağlı ve rAF kısıtlı olduğu ortamlarda SESSİZCE
    // hiç ilerlemiyor — bu düğmenin tek işi kaybolan düğüme götürmek olduğu
    // için görsel incelik uğruna başarısız olmasına değmez.
    // Yakınlaştırma kullanıcının mevcut seviyesinden düşürülmüyor; çok uzakken
    // 1'e çekiliyor, yoksa küçük düğüm yine görünmez kalırdı.
    setCenter(node.position.x + width / 2, node.position.y + height / 2, {
      zoom: Math.max(getZoom(), 1),
    });
  };

  const removeRule = (ruleId: string) => {
    deleteRule(ruleId);
    clearFlowNodePosition(ruleId);
  };

  const iconButtonClass =
    'rounded-[var(--radius-control)] p-1 text-content-muted transition-colors hover:bg-content-primary/12 hover:text-content-primary cursor-pointer';

  return (
    <aside
      className="nodrag nopan fixed right-0 top-0 z-[85] flex h-full w-full max-w-xs flex-col border-l border-content-primary/10 bg-surface-800 shadow-2xl"
      aria-label="Namines Flow rules"
    >
      <header className="flex items-center justify-between border-b border-content-primary/10 px-4 py-3">
        <h2 className="flex items-center gap-2 text-sm font-semibold text-content-primary">
          <Zap className="h-4 w-4 text-warning-text" />
          Namines Flow
        </h2>
        <button
          type="button"
          onClick={() => setPanelOpen(false)}
          aria-label="Close the flow list"
          className={iconButtonClass}
        >
          <X className="h-4 w-4" />
        </button>
      </header>

      {paused && (
        <p className="border-b border-warning/30 bg-warning/10 px-4 py-2 text-xs text-warning-text">
          Flows are paused — nothing will fire until you resume.
        </p>
      )}

      {rules.length === 0 ? (
        <p className="px-4 py-6 text-xs leading-relaxed text-content-muted">
          No flows yet. Press <span className="font-semibold text-content-secondary">New</span> on the
          flow bar and click a table, or right-click a table and choose{' '}
          <span className="font-semibold text-content-secondary">Start a Flow here</span>.
        </p>
      ) : (
        <ul className="flex-1 overflow-y-auto p-2">
          {rules.map(rule => (
            <li
              key={rule.id}
              className={`mb-1.5 rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-700 p-2.5 transition-opacity ${
                rule.enabled ? '' : 'opacity-60'
              }`}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="truncate text-xs font-semibold text-content-primary">
                    {tableName(rule.scopeTableId)}
                  </p>
                  <p className="mt-0.5 text-micro text-content-secondary">
                    {TRIGGER_LABEL[rule.triggerType] ?? rule.triggerType}
                    {' → '}
                    <span className="text-warning-text">
                      {ACTION_LABEL[rule.actionType] ?? rule.actionType}
                    </span>
                  </p>
                </div>

                <label className="flex shrink-0 cursor-pointer items-center gap-1 text-micro text-content-muted">
                  <input
                    type="checkbox"
                    checked={rule.enabled}
                    onChange={e => updateRule(rule.id, { enabled: e.target.checked })}
                    className="cursor-pointer accent-warning"
                    aria-label={`Enable the flow on ${tableName(rule.scopeTableId)}`}
                  />
                  On
                </label>
              </div>

              <div className="mt-2 flex items-center gap-0.5">
                <button
                  type="button"
                  onClick={() => focusRule(rule.id)}
                  title="Show on canvas"
                  aria-label="Show this flow on the canvas"
                  className={iconButtonClass}
                >
                  <Crosshair className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => setSelectedRuleId(rule.id)}
                  title="Edit"
                  aria-label="Edit this flow"
                  className={iconButtonClass}
                >
                  <Pencil className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => removeRule(rule.id)}
                  title="Delete"
                  aria-label="Delete this flow"
                  className="ml-auto rounded-[var(--radius-control)] p-1 text-danger-text/80 transition-colors hover:bg-danger/20 hover:text-danger-text cursor-pointer"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </aside>
  );
}
