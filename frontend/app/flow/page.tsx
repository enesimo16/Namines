'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Zap, Plus, Pencil, Play, Trash2, Boxes, CheckCircle2, XCircle, MinusCircle, Workflow,
} from 'lucide-react';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useAutomationStore, type AutomationRule } from '../../store/useAutomationStore';
import { useFlowBarStore } from '../../store/useFlowBarStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useToastStore } from '../../store/useToastStore';
import { testAutomationRule } from '../../lib/automationApi';
import { isDetachedRule } from '../../lib/naminesFlowRuntime';
import { NAMINES_FLOW_TEMPLATES, instantiateTemplate, type NaminesFlowTemplate } from '../../lib/naminesFlowTemplates';
import AutomationRuleDrawer from '../../components/canvas/AutomationRuleDrawer';
import {
  PageShell, PageHeader, Card, Callout, Badge, Button, EmptyState, Switch, Field, inputClass, Select,
} from '../../components/workspace/WorkspaceKit';

const TRIGGER_LABEL: Record<string, string> = {
  TableAdded: 'A table is added',
  TableDeleted: 'A table is deleted',
  ColumnAdded: 'A column is added',
  ColumnDeleted: 'A column is deleted',
  ColumnChanged: 'A column changes',
  RelationAdded: 'A relation is added',
  RelationDeleted: 'A relation is deleted',
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
 * Namines Flow — kendi sayfası.
 *
 * Flow daha önce yalnızca canvas'ın üstünde küçük yüzen bir çubuktu; üst gezinmede
 * "Flow"a tıklamak kullanıcıyı diyagramda bırakıyordu ("hâlâ diagramdayım"). Kurallar
 * tablo düğümlerine bağlı olsa da onları yönetmek bir LİSTE işi: aç/kapa, test et,
 * düzenle, sil, son çalışma durumu. Canvas'taki düğümler ve "Show on canvas" hâlâ var.
 */
export default function FlowPage() {
  const router = useRouter();
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const openAuthModal = useAuthModalStore(s => s.open);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const projectName = useSchemaStore(s => s.projectName);
  // Doğrudan /flow adresi açıldığında (yenileme, yer imi) canvas henüz şemayı yüklememiş
  // olabilir; o durumda aktif projenin kayıtlı şemasına düş — yoksa tablo şablonları
  // "tablo yok" diye kilitli görünür ve "yetim kural" denetimi tabloları bilmez.
  const liveTables = useSchemaStore(s => s.schema?.tables);
  const savedTables = useProjectHistoryStore(s => s.projects.find(p => p.id === s.activeProjectId)?.schema?.tables);
  const tables = liveTables ?? savedTables;
  const showToast = useToastStore(s => s.showToast);

  const rules = useAutomationStore(s => s.rules);
  const loadRules = useAutomationStore(s => s.loadRules);
  const addRule = useAutomationStore(s => s.addRule);
  const updateRule = useAutomationStore(s => s.updateRule);
  const deleteRule = useAutomationStore(s => s.deleteRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);

  const paused = useFlowBarStore(s => s.paused);
  const togglePaused = useFlowBarStore(s => s.togglePaused);

  const [templateTableId, setTemplateTableId] = useState('');
  const [testingId, setTestingId] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  useEffect(() => {
    if (isAuthenticated && activeProjectId) void loadRules(activeProjectId);
  }, [isAuthenticated, activeProjectId, loadRules]);

  // Şablon çapası: kullanıcı seçmediyse ilk tablo.
  const anchorTableId = templateTableId || tables?.[0]?.id || '';

  const tableName = (tableId: string) =>
    tableId === '' ? 'Whole project' : tables?.find(t => t.id === tableId)?.name ?? 'Unknown table';

  const detached = useMemo(() => rules.filter(r => isDetachedRule(r, tables)), [rules, tables]);

  const applyTemplate = (template: NaminesFlowTemplate) => {
    const scopeTableId = template.requiresTable ? anchorTableId : '';
    const { conditions, actions } = instantiateTemplate(template);
    const ruleId = addRule(scopeTableId, template.triggerType, actions[0]?.actionType ?? 'Toast');
    updateRule(ruleId, { name: template.name, conditions, actions });
    // Çekmece açılıyor ki eksik alan (ör. webhook URL'i) hemen görülsün.
    setSelectedRuleId(ruleId);
  };

  const newBlankFlow = () => {
    const ruleId = addRule('', 'TableAdded', 'Toast');
    setSelectedRuleId(ruleId);
  };

  const runTest = async (ruleId: string) => {
    setTestingId(ruleId);
    try {
      const result = await testAutomationRule(ruleId);
      const failed = result.runs.filter(r => r.status === 'Failed').length;
      const skipped = result.runs.filter(r => r.status === 'Skipped').length;
      if (result.runs.length === 0) showToast('This flow has no server-side steps to run.', 'info');
      else if (failed > 0) showToast(`Test finished: ${failed} step(s) failed. Open the flow for details.`, 'error');
      else if (skipped > 0) showToast(`Test finished: ${skipped} step(s) were skipped. Open the flow for details.`, 'warning');
      else showToast('Test finished: every server-side step succeeded.', 'success');
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

  const removeRule = (ruleId: string) => {
    deleteRule(ruleId);
    setConfirmDeleteId(null);
  };

  if (!isAuthenticated) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState icon={Zap} title="Sign in to use Namines Flow"
          description="Flows run on the server when your schema changes, so they belong to an account."
          action={<Button variant="primary" onClick={openAuthModal}>Log in / Sign up</Button>} />
      </PageShell>
    );
  }

  if (!activeProjectId) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState icon={Boxes} title="Open a project first"
          description="Flows are attached to a project. Open one from the workspace menu, or design a schema on the diagram."
          action={<Button variant="primary" icon={Boxes} onClick={() => router.push('/canvas')}>Go to the diagram</Button>} />
      </PageShell>
    );
  }

  return (
    <PageShell>
      <div id="flow-header">
        <PageHeader
          icon={Zap}
          title="Flow"
          description="Automations that react when your schema changes — notify a channel, call a webhook, lint the change, or seed sample data."
          meta={<Badge tone="neutral" icon={Workflow}>{projectName}</Badge>}
          actions={
            <div className="flex items-center gap-2">
              <label id="flow-pause-switch" className="flex items-center gap-2.5 h-9 px-3 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 text-xs font-semibold text-content-secondary shadow-xs cursor-pointer">
                <Switch checked={paused} onChange={togglePaused} label="Pause flow reactions in this browser" />
                {paused ? 'Paused' : 'Running'}
              </label>
              <Button id="flow-new-btn" variant="primary" icon={Plus} onClick={newBlankFlow}>New flow</Button>
            </div>
          }
        />
      </div>

      {paused && (
        <Callout tone="neutral" title="Flows are paused in this browser">
          Toasts and canvas animations are muted until you resume. Server-side steps are not affected.
        </Callout>
      )}

      {detached.length > 0 && (
        <Callout
          tone="danger"
          title={`${detached.length} flow${detached.length === 1 ? '' : 's'} can never run`}
          action={
            <Button variant="danger" size="sm" onClick={() => detached.forEach(r => removeRule(r.id))}>Remove them</Button>
          }
        >
          They point at tables that are no longer in this schema.
        </Callout>
      )}

      <Card
        id="flow-rules-list"
        title="Your flows"
        description={rules.length === 0 ? undefined : `${rules.length} flow${rules.length === 1 ? '' : 's'} in this project`}
        flush
      >
        {rules.length === 0 ? (
          <EmptyState icon={Zap} title="No flows yet"
            description="Start from a template below, or create a blank flow and choose the trigger and steps yourself." />
        ) : (
          <ul className="divide-y divide-line">
            {rules.map(rule => (
              <RuleRow
                key={rule.id}
                rule={rule}
                scopeLabel={tableName(rule.scopeTableId)}
                detached={isDetachedRule(rule, tables)}
                testing={testingId === rule.id}
                confirmingDelete={confirmDeleteId === rule.id}
                onToggle={enabled => updateRule(rule.id, { enabled })}
                onEdit={() => setSelectedRuleId(rule.id)}
                onTest={() => void runTest(rule.id)}
                onAskDelete={() => setConfirmDeleteId(rule.id)}
                onCancelDelete={() => setConfirmDeleteId(null)}
                onDelete={() => removeRule(rule.id)}
              />
            ))}
          </ul>
        )}
      </Card>

      <Card
        id="flow-templates"
        title="Start from a template"
        description="Each template creates a real flow and opens it so you can fill in what is missing (a webhook URL, for example)."
        actions={
          tables && tables.length > 0 ? (
            <Field label="Attach table-based templates to">
              <Select
                value={anchorTableId}
                onChange={e => setTemplateTableId(e.target.value)}
                className="w-52"
                aria-label="Table that table-based templates attach to"
              >
                {tables.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
              </Select>
            </Field>
          ) : undefined
        }
      >
        <ul className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {NAMINES_FLOW_TEMPLATES.map(template => {
            const blocked = template.requiresTable && !anchorTableId;
            return (
              <li key={template.id}>
                <button
                  type="button"
                  disabled={blocked}
                  onClick={() => applyTemplate(template)}
                  className="group w-full h-full text-left rounded-[var(--radius-card)] border border-line-strong bg-surface-800 hover:bg-surface-700/80 p-4 transition-all hover:border-line-strong cursor-pointer shadow-xs disabled:opacity-45 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-focus-ring)]"
                >
                  <span className="flex items-start justify-between gap-3">
                    <span className="text-xs font-bold text-content-primary">{template.label}</span>
                    <Plus className="w-4 h-4 shrink-0 text-content-muted group-hover:text-content-primary transition-colors" aria-hidden="true" />
                  </span>
                  <span className="mt-1 block text-micro leading-relaxed text-content-muted">
                    {blocked ? 'Add a table to the diagram to use this template.' : template.description}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      </Card>

      <AutomationRuleDrawer />
    </PageShell>
  );
}

function LastRunBadge({ rule }: { rule: AutomationRule }) {
  const run = rule.lastRun;
  if (!run) return <Badge tone="neutral" icon={MinusCircle}>Never run</Badge>;
  const when = new Date(run.triggeredAt).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' });
  if (run.status === 'Success') return <Badge tone="success" icon={CheckCircle2} title={when}>Last run succeeded</Badge>;
  if (run.status === 'Failed') return <Badge tone="danger" icon={XCircle} title={run.errorMessage ?? when}>Last run failed</Badge>;
  return <Badge tone="neutral" icon={MinusCircle} title={when}>Last run {run.status.toLowerCase()}</Badge>;
}

function RuleRow({
  rule, scopeLabel, detached, testing, confirmingDelete,
  onToggle, onEdit, onTest, onAskDelete, onCancelDelete, onDelete,
}: {
  rule: AutomationRule;
  scopeLabel: string;
  detached: boolean;
  testing: boolean;
  confirmingDelete: boolean;
  onToggle: (enabled: boolean) => void;
  onEdit: () => void;
  onTest: () => void;
  onAskDelete: () => void;
  onCancelDelete: () => void;
  onDelete: () => void;
}) {
  const steps = rule.actions.length === 0
    ? 'No action'
    : rule.actions.map(a => ACTION_LABEL[a.actionType] ?? (a.actionType || 'Invalid action')).join(' → ');

  return (
    <li className={`px-5 py-4 transition-opacity ${rule.enabled ? '' : 'opacity-60'}`}>
      <div className="flex items-start gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-sm font-semibold text-content-primary truncate">{rule.name || 'Untitled flow'}</h3>
            <Badge tone="neutral">{scopeLabel}</Badge>
            <LastRunBadge rule={rule} />
          </div>
          <p className="mt-1.5 text-[13px] text-content-secondary">
            <span className="text-content-muted">When</span> {TRIGGER_LABEL[rule.triggerType]?.toLowerCase() ?? rule.triggerType}
            <span className="text-content-muted"> · then </span>{steps}
          </p>
          {detached && (
            <p className="mt-1.5 text-[12px] text-danger-text">
              The table this flow was attached to is no longer in the schema — it can never run.
            </p>
          )}
          {rule.lastRun?.status === 'Failed' && rule.lastRun.errorMessage && (
            <p className="mt-1.5 text-[12px] text-danger-text break-words">{rule.lastRun.errorMessage}</p>
          )}
        </div>
        <Switch checked={rule.enabled} onChange={onToggle} label={`Turn ${rule.name || 'this flow'} ${rule.enabled ? 'off' : 'on'}`} />
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        {confirmingDelete ? (
          <>
            <span className="text-[13px] text-content-secondary mr-1">Delete this flow?</span>
            <Button variant="danger" size="sm" icon={Trash2} onClick={onDelete}>Delete</Button>
            <Button variant="ghost" size="sm" onClick={onCancelDelete}>Cancel</Button>
          </>
        ) : (
          <>
            <Button size="sm" icon={Pencil} onClick={onEdit}>Edit</Button>
            <Button size="sm" icon={Play} busy={testing} onClick={onTest} title="Run the server-side steps now">Run test</Button>
            <span className="flex-1" />
            <Button variant="ghost" size="sm" icon={Trash2} onClick={onAskDelete} aria-label="Delete this flow" />
          </>
        )}
      </div>
    </li>
  );
}
