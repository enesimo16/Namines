'use client';

import { useNodes, useReactFlow } from '@xyflow/react';
import { useState } from 'react';
import { Crosshair, Pencil, Play, Trash2, X, Zap } from 'lucide-react';
import { useFlowBarStore } from '../../../store/useFlowBarStore';
import { useAutomationStore } from '../../../store/useAutomationStore';
import { useFlowNodePositionStore } from '../../../store/useFlowNodePositionStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { NAMINES_FLOW_TEMPLATES, type NaminesFlowTemplate } from '../../../lib/naminesFlowTemplates';
import { testAutomationRule } from '../../../lib/automationApi';
import { useToastStore } from '../../../store/useToastStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';

const TRIGGER_LABEL: Record<string, string> = {
  TableAdded: 'Table added',
  TableDeleted: 'Table deleted',
  ColumnAdded: 'Column added',
  ColumnDeleted: 'Column deleted',
  ColumnChanged: 'Column changed',
  RelationAdded: 'Relation added',
  RelationDeleted: 'Relation deleted',
};

const LAST_RUN_STYLE: Record<string, string> = {
  Success: 'text-success-text',
  Failed: 'text-danger-text',
  Skipped: 'text-warning-text',
};

const ACTION_LABEL: Record<string, string> = {
  Toast: 'Notify',
  Slack: 'Slack',
  Discord: 'Discord',
  Webhook: 'Webhook',
  Lint: 'Lint',
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
  const addRule = useAutomationStore(s => s.addRule);
  const updateRule = useAutomationStore(s => s.updateRule);
  const deleteRule = useAutomationStore(s => s.deleteRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);
  const clearFlowNodePosition = useFlowNodePositionStore(s => s.clearPosition);

  const tables = useSchemaStore(s => s.schema?.tables);
  const loadRules = useAutomationStore(s => s.loadRules);
  const showToast = useToastStore(s => s.showToast);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const [testingId, setTestingId] = useState<string | null>(null);

  // Tabloya bağlı şablonların çapası: canvas'ta seçili olan tablo.
  // `useNodes` REAKTİF — `getNodes()` render sırasında bir kez okunurdu ve
  // kullanıcı başka bir tablo seçtiğinde şablon düğmeleri eski durumda
  // (yanlış şekilde devre dışı ya da yanlış tabloya bağlı) kalırdı.
  const selectedTableId = useNodes().find(n => n.selected && n.type === 'tableNode')?.id;

  if (!panelOpen) return null;

  const tableName = (tableId: string) =>
    tableId === ''
      ? 'Whole project'
      : tables?.find(t => t.id === tableId)?.name ?? 'unknown table';

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

  /**
   * Listeden doğrudan test. Sonuç toast olarak veriliyor — panelde satır
   * başına ayrıntılı çıktı göstermek listeyi okunmaz hâle getirirdi; tam
   * geçmiş zaten çekmecede.
   */
  const runTest = async (ruleId: string) => {
    setTestingId(ruleId);
    try {
      const result = await testAutomationRule(ruleId);
      const failed = result.runs.filter(r => r.status === 'Failed').length;
      const skipped = result.runs.filter(r => r.status === 'Skipped').length;
      if (result.runs.length === 0) {
        showToast('This flow has no server-side steps to run.', 'info');
      } else if (failed > 0) {
        showToast(`Test finished: ${failed} step(s) failed. Open the flow for details.`, 'error');
      } else if (skipped > 0) {
        showToast(`Test finished: ${skipped} step(s) were skipped. Open the flow for details.`, 'warning');
      } else {
        showToast('Test finished: every server-side step succeeded.', 'success');
      }
      // Rozet sunucudan geliyor; testten sonra tazelenmezse eski durumu
      // gösterirdi.
      if (activeProjectId) void loadRules(activeProjectId);
    } catch (err) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      showToast(
        status === 429
          ? 'Too many test runs for this flow — wait a minute and try again.'
          : 'The test run could not be started.',
        'error',
      );
    } finally {
      setTestingId(null);
    }
  };

  /**
   * Şablonu gerçek bir kurala çeviriyor. `addRule` yalnızca tek aksiyonlu bir
   * iskelet kurabildiği için (imzası öyle), zincir ve koşullar hemen ardından
   * `updateRule` ile yazılıyor — store zaten iyimser, ikisi tek karede
   * birleşiyor.
   */
  const applyTemplate = (template: NaminesFlowTemplate) => {
    const scopeTableId = template.requiresTable ? (selectedTableId ?? '') : '';
    const firstAction = template.actions[0] ?? { actionType: 'Toast' as const, actionConfig: {} };
    const ruleId = addRule(scopeTableId, template.triggerType, firstAction.actionType);
    updateRule(ruleId, {
      name: template.name,
      conditions: template.conditions,
      actions: template.actions,
    });
    // Çekmece açılıyor ki eksik kalan alan (ör. webhook URL'i) hemen görülsün.
    setSelectedRuleId(ruleId);
    setPanelOpen(false);
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

      <section className="border-b border-content-primary/10 p-2">
        <h3 className="px-1 pb-1 text-micro font-semibold uppercase tracking-wider text-content-muted">
          Start from a template
        </h3>
        <ul className="flex flex-col gap-1">
          {NAMINES_FLOW_TEMPLATES.map(template => {
            // Tabloya bağlı bir şablon, hangi tabloya bağlanacağı belli
            // olmadan kurulamaz. Düğmeyi gizlemek yerine devre dışı bırakıp
            // SEBEBİNİ söylüyoruz — aksi hâlde şablonun neden görünmediği
            // anlaşılmazdı.
            const blocked = template.requiresTable && !selectedTableId;
            return (
              <li key={template.id}>
                <button
                  type="button"
                  disabled={blocked}
                  onClick={() => applyTemplate(template)}
                  title={blocked ? 'Select a table on the canvas first' : template.description}
                  className="w-full rounded-[var(--radius-control)] border border-content-primary/10 bg-surface-700 px-2 py-1.5 text-left transition-colors hover:border-warning/50 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  <span className="block text-micro font-semibold text-content-primary">{template.label}</span>
                  <span className="block text-micro leading-snug text-content-muted">
                    {blocked ? 'Select a table on the canvas first.' : template.description}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      </section>

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
                    {rule.name || tableName(rule.scopeTableId)}
                  </p>
                  <p className="mt-0.5 text-micro text-content-secondary">
                    {TRIGGER_LABEL[rule.triggerType] ?? rule.triggerType}
                    {' → '}
                    <span className="text-warning-text">
                      {rule.actions.length === 0
                        ? 'no action'
                        : rule.actions.map(a => ACTION_LABEL[a.actionType] ?? a.actionType).join(' + ')}
                    </span>
                  </p>
                  {/* Son çalışma kural listesiyle birlikte geliyor (tek sorgu),
                      böylece "hangi kural patlıyor" sorusu kuralları tek tek
                      açmadan cevaplanabiliyor. */}
                  {rule.lastRun && (
                    <p className={`mt-0.5 text-micro ${LAST_RUN_STYLE[rule.lastRun.status] ?? 'text-content-muted'}`}>
                      Last run: {rule.lastRun.status}
                      {rule.lastRun.errorMessage ? ` — ${rule.lastRun.errorMessage}` : ''}
                    </p>
                  )}
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
                  onClick={() => void runTest(rule.id)}
                  disabled={testingId === rule.id}
                  title="Run the server-side steps now"
                  aria-label="Test this flow now"
                  className={iconButtonClass}
                >
                  <Play className={`h-3.5 w-3.5 ${testingId === rule.id ? 'opacity-50' : ''}`} />
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
