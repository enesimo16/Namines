'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import {
  Zap,
  Plus,
  Pencil,
  Play,
  Trash2,
  Boxes,
  CheckCircle2,
  XCircle,
  MinusCircle,
  Workflow,
  Lock,
  ArrowLeft,
  ArrowUpRight,
  Loader2,
  Database,
} from 'lucide-react';
import { authService } from '../../services/api';
import type { CloudProjectDto } from '../../types/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useAutomationStore, type AutomationRule } from '../../store/useAutomationStore';
import { useFlowBarStore } from '../../store/useFlowBarStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';
import { useToastStore } from '../../store/useToastStore';
import { testAutomationRule } from '../../lib/automationApi';
import { isDetachedRule } from '../../lib/naminesFlowRuntime';
import {
  NAMINES_FLOW_TEMPLATES,
  instantiateTemplate,
  type NaminesFlowTemplate,
} from '../../lib/naminesFlowTemplates';
import AutomationRuleDrawer from '../../components/canvas/AutomationRuleDrawer';
import { ModuleSymbol, ModuleWordmark } from '../../components/layout/ModuleLogo';
import {
  PageShell,
  PageHeader,
  Card,
  Callout,
  Badge,
  Button,
  EmptyState,
  StatTile,
  Switch,
  Field,
  Select,
  DataTable,
  Td,
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

export default function FlowPage() {
  return (
    <Suspense
      fallback={
        <PageShell>
          <Card>
            <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading Flow…
            </div>
          </Card>
        </PageShell>
      }
    >
      <FlowPageContent />
    </Suspense>
  );
}

/**
 * Faz 5 — Namines Flow:
 * 1. `/flow?project=<id>` (Projeye Kilitli Oturum):
 *    Ekmek kırıntısı: `flow / {owner} / {projectName}` formatındadır.
 *    `flow` linkine tıklandığında genel konsola dönülür.
 * 2. `/flow` (Parametresiz Genel Yönetim Düzlemi):
 *    Tüm projelerin otomasyon dizini, genel tarayıcı tepki anahtarı ve şablon kataloğu.
 */
function FlowPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const projectParam = searchParams.get('project');

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const currentUser = useAuthStore((s) => s.user);
  const openAuthModal = useAuthModalStore((s) => s.open);
  const activeProjectId = useProjectHistoryStore((s) => s.activeProjectId);
  const localProjects = useProjectHistoryStore((s) => s.projects);
  const schemaProjectName = useSchemaStore((s) => s.projectName);
  const { scopedProjectId, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession } =
    useActiveProjectStore();

  const [cloudProjects, setCloudProjects] = useState<CloudProjectDto[] | null>(null);

  const loadCloud = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      const list = await authService.getCloudProjects();
      setCloudProjects(list);
    } catch {
      setCloudProjects([]);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    loadCloud();
  }, [loadCloud]);

  const targetProjectId = projectParam || null;

  const liveTables = useSchemaStore((s) => s.schema?.tables);
  const savedTables = useProjectHistoryStore(
    (s) => s.projects.find((p) => p.id === (targetProjectId ?? s.activeProjectId))?.schema?.tables,
  );
  const tables = liveTables ?? savedTables;
  const showToast = useToastStore((s) => s.showToast);

  const rules = useAutomationStore((s) => s.rules);
  const loadRules = useAutomationStore((s) => s.loadRules);
  const addRule = useAutomationStore((s) => s.addRule);
  const updateRule = useAutomationStore((s) => s.updateRule);
  const deleteRule = useAutomationStore((s) => s.deleteRule);
  const setSelectedRuleId = useAutomationStore((s) => s.setSelectedRuleId);

  const paused = useFlowBarStore((s) => s.paused);
  const togglePaused = useFlowBarStore((s) => s.togglePaused);

  const [templateTableId, setTemplateTableId] = useState('');
  const [testingId, setTestingId] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  // URL ?project=<id> senkronizasyonu
  useEffect(() => {
    if (projectParam) {
      const cloudMatch = cloudProjects?.find((p) => p.id === projectParam);
      const localMatch = localProjects.find((p) => p.id === projectParam);
      const resolvedName =
        cloudMatch?.name ||
        localMatch?.name ||
        scopedProjectName ||
        scopedHubRepo?.name ||
        schemaProjectName ||
        'Project Workspace';
      // activeProjectId BİLEREK değiştirilmiyor: şema store'u başka projeye ait olabilir ve
      // canvas autosave'i o şemayı bu projenin üzerine yazardı.
      setScopedProject(projectParam, resolvedName);
    } else {
      clearProjectSession();
    }
  }, [
    projectParam,
    cloudProjects,
    localProjects,
    schemaProjectName,
    scopedProjectName,
    scopedHubRepo,
    setScopedProject,
    clearProjectSession,
  ]);

  // Kurallar yalnızca proje/oturum değişince çekilir (autosave her tetiklendiğinde değil)
  const rulesProjectId = projectParam || activeProjectId;
  useEffect(() => {
    if (isAuthenticated && rulesProjectId) {
      void loadRules(rulesProjectId);
    }
  }, [isAuthenticated, rulesProjectId, loadRules]);

  const anchorTableId = templateTableId || tables?.[0]?.id || '';

  const tableName = (tableId: string) =>
    tableId === '' ? 'Whole project' : (tables?.find((t) => t.id === tableId)?.name ?? 'Unknown table');

  const detached = useMemo(() => rules.filter((r) => isDetachedRule(r, tables)), [rules, tables]);

  // Hook erken dönüşlerden önce olmalı (kural ihlali = render sırasında hook sayısı değişir)
  const mergedProjects = useMemo(() => {
    const map = new Map<
      string,
      { id: string; name: string; dbType: string; tableCount: number; hasConnection: boolean }
    >();
    for (const lp of localProjects) {
      map.set(lp.id, {
        id: lp.id,
        name: lp.name,
        dbType: lp.dbType || 'PostgreSQL',
        tableCount: lp.schema?.tables?.length ?? 0,
        hasConnection: false,
      });
    }
    for (const cp of cloudProjects ?? []) {
      const existing = map.get(cp.id);
      map.set(cp.id, {
        id: cp.id,
        name: cp.name,
        dbType: cp.dbType || existing?.dbType || 'PostgreSQL',
        tableCount: existing?.tableCount ?? 0,
        hasConnection: Boolean(cp.hasConnection),
      });
    }
    return Array.from(map.values());
  }, [localProjects, cloudProjects]);

  const applyTemplate = (template: NaminesFlowTemplate) => {
    const scopeTableId = template.requiresTable ? anchorTableId : '';
    const { conditions, actions } = instantiateTemplate(template);
    const ruleId = addRule(scopeTableId, template.triggerType, actions[0]?.actionType ?? 'Toast');
    updateRule(ruleId, { name: template.name, conditions, actions });
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
      const failed = result.runs.filter((r) => r.status === 'Failed').length;
      const skipped = result.runs.filter((r) => r.status === 'Skipped').length;
      if (result.runs.length === 0) showToast('This flow has no server-side steps to run.', 'info');
      else if (failed > 0)
        showToast(`Test finished: ${failed} step(s) failed. Open the flow for details.`, 'error');
      else if (skipped > 0)
        showToast(`Test finished: ${skipped} step(s) were skipped. Open the flow for details.`, 'warning');
      else showToast('Test finished: every server-side step succeeded.', 'success');
      const pid = projectParam || activeProjectId;
      if (pid) void loadRules(pid);
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
        <EmptyState
          icon={Zap}
          title="Sign in to use Namines Flow"
          description="Flows run on the server when your schema changes, so they belong to an account."
          action={
            <Button variant="primary" onClick={openAuthModal}>
              Log in / Sign up
            </Button>
          }
        />
      </PageShell>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // MODE A: PROJECT-SCOPED FLOW (/flow?project=<id>)
  // ─────────────────────────────────────────────────────────────────────────
  if (projectParam) {
    const lockedCloud = cloudProjects?.find((p) => p.id === projectParam);
    const lockedLocal = localProjects.find((p) => p.id === projectParam);
    const resolvedProjectName =
      scopedProjectName ||
      lockedCloud?.name ||
      lockedLocal?.name ||
      scopedHubRepo?.name ||
      schemaProjectName ||
      projectParam;
    const resolvedOwner = scopedHubRepo?.owner || currentUser?.username || 'workspace';

    return (
      <PageShell>
        {/* Top Breadcrumb (Matching Hub format: flow / owner / ProjectName) */}
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-line">
          <div className="flex items-center flex-wrap gap-2 text-sm sm:text-base">
            <ModuleSymbol module="flow" size={20} />
            <Link
              href="/flow"
              onClick={() => clearProjectSession()}
              className="group inline-flex items-center text-content-muted hover:text-content-primary transition-colors cursor-pointer"
            >
              <ModuleWordmark module="flow" height={13} className="text-content-muted group-hover:text-content-primary transition-colors" />
            </Link>
            <span className="text-content-muted">/</span>
            <span className="text-content-secondary font-mono">{resolvedOwner}</span>
            <span className="text-content-muted">/</span>
            <h1 className="font-sans font-bold text-content-primary tracking-tight text-base sm:text-lg">
              {resolvedProjectName}
            </h1>
          </div>

          <div className="flex items-center gap-2">
            <label
              id="flow-pause-switch"
              className="flex items-center gap-2 h-8 px-2.5 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 text-xs font-semibold text-content-secondary shadow-xs cursor-pointer"
            >
              <Switch
                checked={paused}
                onChange={togglePaused}
                label="Pause flow reactions in this browser"
              />
              <span className="text-micro font-mono">{paused ? 'Paused' : 'Running'}</span>
            </label>
            <Button id="flow-new-btn" size="sm" variant="primary" icon={Plus} onClick={newBlankFlow}>
              New flow
            </Button>
          </div>
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
              <Button
                variant="danger"
                size="sm"
                onClick={() => detached.forEach((r) => removeRule(r.id))}
              >
                Remove them
              </Button>
            }
          >
            They point at tables that are no longer in this schema.
          </Callout>
        )}

        <Card
          id="flow-rules-list"
          title="Project flows"
          description={
            rules.length === 0
              ? undefined
              : `${rules.length} flow${rules.length === 1 ? '' : 's'} in ${resolvedProjectName}`
          }
          flush
        >
          {rules.length === 0 ? (
            <EmptyState
              icon={Zap}
              title="No flows yet"
              description="Start from a template below, or create a blank flow and choose the trigger and steps yourself."
            />
          ) : (
            <ul className="divide-y divide-line">
              {rules.map((rule) => (
                <RuleRow
                  key={rule.id}
                  rule={rule}
                  scopeLabel={tableName(rule.scopeTableId)}
                  detached={isDetachedRule(rule, tables)}
                  testing={testingId === rule.id}
                  confirmingDelete={confirmDeleteId === rule.id}
                  onToggle={(enabled) => updateRule(rule.id, { enabled })}
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
                  onChange={(e) => setTemplateTableId(e.target.value)}
                  className="w-52"
                  aria-label="Table that table-based templates attach to"
                >
                  {tables.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </Select>
              </Field>
            ) : undefined
          }
        >
          <ul className="grid grid-cols-1 md:grid-cols-2 gap-3">
            {NAMINES_FLOW_TEMPLATES.map((template) => {
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
                      <Plus
                        className="w-4 h-4 shrink-0 text-content-muted group-hover:text-content-primary transition-colors"
                        aria-hidden="true"
                      />
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

  // ─────────────────────────────────────────────────────────────────────────
  // MODE B: GLOBAL FLOW AUTOMATION CONSOLE (/flow without ?project=)
  // ─────────────────────────────────────────────────────────────────────────
  return (
    <PageShell>
      <div id="flow-header">
        <PageHeader
          icon={Zap}
          title="Global Flow — Event & Lifecycle Orchestrator"
          description="Organization-wide schema event triggers, webhook dispatches, automated DBA checks, and notification pipelines across all projects."
          actions={
            <label
              id="flow-pause-switch"
              className="flex items-center gap-2.5 h-9 px-3 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 text-xs font-semibold text-content-secondary shadow-xs cursor-pointer"
            >
              <Switch
                checked={paused}
                onChange={togglePaused}
                label="Pause flow reactions in this browser"
              />
              {paused ? 'Browser Reactions Paused' : 'Browser Reactions Running'}
            </label>
          }
        />
      </div>

      {/* Global Flow Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatTile
          label="Workspace Projects"
          value={cloudProjects ? mergedProjects.length : '—'}
          hint="Available for Flow rules"
        />
        <StatTile
          label="Runtime Status"
          value={
            <Badge tone={paused ? 'neutral' : 'success'} icon={CheckCircle2}>
              {paused ? 'Paused' : 'Active'}
            </Badge>
          }
          hint="Client + Server dispatch"
        />
        <StatTile
          label="Flow Templates"
          value={NAMINES_FLOW_TEMPLATES.length}
          hint="Slack, Webhook, Lint, Seed"
        />
        <StatTile
          label="Active Session Flows"
          value={rules.length}
          hint={activeProjectId ? 'In last active project' : 'Select a project below'}
        />
      </div>

      <Card
        title="All Projects — Automation Directory"
        description="Select a project to open its dedicated Flow workspace, inspect execution logs, and attach schema triggers."
        flush
      >
        {!cloudProjects ? (
          <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading projects…
          </div>
        ) : mergedProjects.length === 0 ? (
          <EmptyState
            icon={Boxes}
            title="No projects yet"
            description="Create a project on the Diagram Canvas to attach schema event flows."
            action={
              <Button variant="primary" icon={Boxes} onClick={() => router.push('/canvas')}>
                Go to Diagram Canvas
              </Button>
            }
          />
        ) : (
          <DataTable
            caption="Projects available for Namines Flow"
            columns={[
              { label: 'Project' },
              { label: 'Engine' },
              { label: 'Tables' },
              { label: 'Live DB' },
              { label: 'Actions', align: 'right' },
            ]}
          >
            {mergedProjects.map((p) => (
              <tr key={p.id} className="hover:bg-surface-700/30 transition-colors">
                <Td>
                  <div className="flex items-center gap-2">
                    <Workflow className="w-3.5 h-3.5 text-accent-text shrink-0" />
                    <span className="font-semibold text-content-primary">{p.name}</span>
                  </div>
                </Td>
                <Td mono>{p.dbType}</Td>
                <Td mono>{p.tableCount > 0 ? `${p.tableCount} tables` : '—'}</Td>
                <Td>
                  {p.hasConnection ? (
                    <Badge tone="success" icon={Database}>
                      Connected
                    </Badge>
                  ) : (
                    <Badge tone="neutral">Schema only</Badge>
                  )}
                </Td>
                <Td align="right">
                  <Button
                    size="sm"
                    variant="secondary"
                    icon={ArrowUpRight}
                    onClick={() => {
                      setScopedProject(p.id, p.name);
                      router.push(`/flow?project=${encodeURIComponent(p.id)}`);
                    }}
                  >
                    Manage Project Flows
                  </Button>
                </Td>
              </tr>
            ))}
          </DataTable>
        )}
      </Card>

      <Card
        id="flow-templates"
        title="Organization Automation Templates"
        description="Standard event-driven blueprints ready to attach to any project in your workspace."
      >
        <ul className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {NAMINES_FLOW_TEMPLATES.map((template) => (
            <li
              key={template.id}
              className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-4 flex flex-col justify-between gap-2"
            >
              <div>
                <div className="flex items-center justify-between gap-2">
                  <span className="text-xs font-bold text-content-primary">{template.label}</span>
                  <Badge tone="neutral">{TRIGGER_LABEL[template.triggerType] ?? template.triggerType}</Badge>
                </div>
                <p className="mt-1 text-micro leading-relaxed text-content-muted">
                  {template.description}
                </p>
              </div>
              {mergedProjects.length > 0 && (
                <div className="pt-2 border-t border-line flex items-center justify-end">
                  <button
                    type="button"
                    onClick={() => {
                      const pid = activeProjectId || mergedProjects[0].id;
                      setScopedProject(pid, null);
                      router.push(`/flow?project=${encodeURIComponent(pid)}`);
                    }}
                    className="inline-flex items-center gap-1 text-xs font-semibold text-accent-text hover:underline cursor-pointer"
                  >
                    <span>Configure in Project</span>
                    <ArrowUpRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      </Card>
    </PageShell>
  );
}

function LastRunBadge({ rule }: { rule: AutomationRule }) {
  const run = rule.lastRun;
  if (!run) return <Badge tone="neutral" icon={MinusCircle}>Never run</Badge>;
  const when = new Date(run.triggeredAt).toLocaleString('en-US', {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
  if (run.status === 'Success')
    return (
      <Badge tone="success" icon={CheckCircle2} title={when}>
        Last run succeeded
      </Badge>
    );
  if (run.status === 'Failed')
    return (
      <Badge tone="danger" icon={XCircle} title={run.errorMessage ?? when}>
        Last run failed
      </Badge>
    );
  return (
    <Badge tone="neutral" icon={MinusCircle} title={when}>
      Last run {run.status.toLowerCase()}
    </Badge>
  );
}

function RuleRow({
  rule,
  scopeLabel,
  detached,
  testing,
  confirmingDelete,
  onToggle,
  onEdit,
  onTest,
  onAskDelete,
  onCancelDelete,
  onDelete,
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
  const steps =
    rule.actions.length === 0
      ? 'No action'
      : rule.actions
          .map((a) => ACTION_LABEL[a.actionType] ?? (a.actionType || 'Invalid action'))
          .join(' → ');

  return (
    <li className={`px-5 py-4 transition-opacity ${rule.enabled ? '' : 'opacity-60'}`}>
      <div className="flex items-start gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-sm font-semibold text-content-primary truncate">
              {rule.name || 'Untitled flow'}
            </h3>
            <Badge tone="neutral">{scopeLabel}</Badge>
            <LastRunBadge rule={rule} />
          </div>
          <p className="mt-1.5 text-[13px] text-content-secondary">
            <span className="text-content-muted">When</span>{' '}
            {TRIGGER_LABEL[rule.triggerType]?.toLowerCase() ?? rule.triggerType}
            <span className="text-content-muted"> · then </span>
            {steps}
          </p>
          {detached && (
            <p className="mt-1.5 text-[12px] text-danger-text">
              The table this flow was attached to is no longer in the schema — it can never run.
            </p>
          )}
          {rule.lastRun?.status === 'Failed' && rule.lastRun.errorMessage && (
            <p className="mt-1.5 text-[12px] text-danger-text break-words">
              {rule.lastRun.errorMessage}
            </p>
          )}
        </div>
        <Switch
          checked={rule.enabled}
          onChange={onToggle}
          label={`Turn ${rule.name || 'this flow'} ${rule.enabled ? 'off' : 'on'}`}
        />
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        {confirmingDelete ? (
          <>
            <span className="text-[13px] text-content-secondary mr-1">Delete this flow?</span>
            <Button variant="danger" size="sm" icon={Trash2} onClick={onDelete}>
              Delete
            </Button>
            <Button variant="ghost" size="sm" onClick={onCancelDelete}>
              Cancel
            </Button>
          </>
        ) : (
          <>
            <Button size="sm" icon={Pencil} onClick={onEdit}>
              Edit
            </Button>
            <Button
              size="sm"
              icon={Play}
              busy={testing}
              onClick={onTest}
              title="Run the server-side steps now"
            >
              Run test
            </Button>
            <span className="flex-1" />
            <Button
              variant="ghost"
              size="sm"
              icon={Trash2}
              onClick={onAskDelete}
              aria-label="Delete this flow"
            />
          </>
        )}
      </div>
    </li>
  );
}
