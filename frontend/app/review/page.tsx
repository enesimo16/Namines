'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  GitPullRequest, Loader2, ArrowRight, Users, KeyRound, ShieldCheck, CheckCircle2, XCircle, Clock, Boxes,
  AlertTriangle,
} from 'lucide-react';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { changeRequestService, authService } from '../../services/api';
import { ChangeRequestSummary, RiskLevel } from '../../types/changeRequest';
import { useToastStore } from '../../store/useToastStore';
import TeamPanel from '../../components/review/TeamPanel';
import GatewayKeyPanel from '../../components/review/GatewayKeyPanel';
import {
  PageShell, PageHeader, Card, Callout, Badge, Button, EmptyState, Switch, Tabs,
} from '../../components/workspace/WorkspaceKit';

const RISK: Record<RiskLevel, { label: string; tone: 'success' | 'neutral' | 'danger'; icon: typeof ShieldCheck }> = {
  Safe: { label: 'Safe', tone: 'success', icon: ShieldCheck },
  Risky: { label: 'Risky', tone: 'neutral', icon: AlertTriangle },
  Destructive: { label: 'Destructive', tone: 'danger', icon: AlertTriangle },
  Breaking: { label: 'Breaking', tone: 'danger', icon: AlertTriangle },
};

const STATUS: Record<string, { label: string; tone: 'success' | 'neutral' | 'danger'; icon: typeof Clock }> = {
  PendingReview: { label: 'Pending review', tone: 'neutral', icon: Clock },
  Approved: { label: 'Approved', tone: 'success', icon: CheckCircle2 },
  Rejected: { label: 'Rejected', tone: 'danger', icon: XCircle },
};

type TabId = 'requests' | 'team' | 'keys';
type Filter = 'all' | 'PendingReview' | 'Approved' | 'Rejected';

/**
 * Review — değişiklik istekleri, onaylayan ekip ve verini okuyan API anahtarları.
 *
 * Bu sayfa eskiden tek bir uzun kolonda dört ayrı şeyi üst üste diziyordu (otomatik
 * onay anahtarı, ekip yönetimi, API anahtarları, sonra asıl liste) — asıl işin, yani
 * bekleyen değişiklik isteklerinin, sayfanın en altına itildiği "iç içe ve çok karışık"
 * bir ekran. Şimdi üç sekme: her seferinde yalnızca bir iş görünüyor.
 */
export default function ChangeRequestListPage() {
  const router = useRouter();
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const projectName = useSchemaStore(s => s.projectName);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const openAuthModal = useAuthModalStore(s => s.open);
  const currentUserEmail = useAuthStore(s => s.user?.email ?? null);

  const showToast = useToastStore(s => s.showToast);
  const [items, setItems] = useState<ChangeRequestSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [autoApproveSafe, setAutoApproveSafeState] = useState<boolean | null>(null);
  const [isTogglingAutoApprove, setIsTogglingAutoApprove] = useState(false);
  const [tab, setTab] = useState<TabId>('requests');
  const [filter, setFilter] = useState<Filter>('all');

  useEffect(() => {
    if (!isAuthenticated || !activeProjectId) {
      setItems([]);
      return;
    }
    changeRequestService.listForProject(activeProjectId)
      .then(setItems)
      .catch((err) => {
        // 404 = proje sunucuda henüz YOK, arıza değil. Şablon açıldıktan hemen
        // sonra bu sayfaya gelindiğinde otomatik kaydetme daha tamamlanmamış
        // oluyor ve kullanıcı ilk temasta hata ekranı görüyordu; oysa saniyeler
        // içinde senkron bitiyor ve yenileyince her şey yerli yerinde.
        if (err?.response?.status === 404) {
          setItems([]);
          return;
        }
        setError('Failed to load change requests.');
      });

    authService.getCloudProjects()
      .then(projects => {
        const project = projects.find(p => p.id === activeProjectId);
        setAutoApproveSafeState(project?.autoApproveSafeChanges ?? false);
      })
      .catch(() => setAutoApproveSafeState(false));
  }, [isAuthenticated, activeProjectId]);

  const handleToggleAutoApprove = async () => {
    if (!activeProjectId || autoApproveSafe === null) return;
    const next = !autoApproveSafe;
    setIsTogglingAutoApprove(true);
    try {
      await changeRequestService.setAutoApproveSafe(activeProjectId, next);
      setAutoApproveSafeState(next);
      showToast(next ? 'Safe changes will now be auto-approved.' : 'Auto-approval for Safe changes disabled.', 'success');
    } catch {
      showToast('Failed to update the setting.', 'error');
    } finally {
      setIsTogglingAutoApprove(false);
    }
  };

  const counts = useMemo(() => {
    const all = items ?? [];
    return {
      all: all.length,
      PendingReview: all.filter(i => i.status === 'PendingReview').length,
      Approved: all.filter(i => i.status === 'Approved').length,
      Rejected: all.filter(i => i.status === 'Rejected').length,
    };
  }, [items]);

  const visible = (items ?? []).filter(i => filter === 'all' || i.status === filter);

  if (!isAuthenticated) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState icon={GitPullRequest} title="Sign in to review changes"
          description="Change requests belong to a project in your account."
          action={<Button variant="primary" onClick={openAuthModal}>Log in / Sign up</Button>} />
      </PageShell>
    );
  }

  if (!activeProjectId) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState icon={Boxes} title="Open a project first"
          description="Change requests are attached to a project. Open one from the workspace menu or the diagram."
          action={<Button variant="primary" icon={Boxes} onClick={() => router.push('/canvas')}>Go to the diagram</Button>} />
      </PageShell>
    );
  }

  return (
    <PageShell width="max-w-4xl">
      <PageHeader
        icon={GitPullRequest}
        title="Review"
        description="Schema changes wait here for approval before they touch your database. Manage who can approve them and which keys can read your data."
        meta={<Badge icon={Boxes}>{projectName}</Badge>}
      />

      <Tabs<TabId>
        ariaLabel="Review sections"
        value={tab}
        onChange={setTab}
        tabs={[
          { value: 'requests', label: 'Change requests', icon: GitPullRequest, count: items ? counts.all : undefined },
          { value: 'team', label: 'Team', icon: Users },
          { value: 'keys', label: 'API keys', icon: KeyRound },
        ]}
      />

      {tab === 'requests' && (
        <div id="tabpanel-requests" role="tabpanel" aria-labelledby="tab-requests" className="space-y-5">
          {autoApproveSafe !== null && (
            <Card>
              <div className="flex items-center justify-between gap-4">
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-content-primary">Auto-approve safe changes</p>
                  <p className="mt-0.5 text-[12px] leading-relaxed text-content-muted">
                    Change requests classified as Safe skip human review and are approved immediately.
                    Risky, destructive and breaking changes always need a person.
                  </p>
                </div>
                <Switch checked={autoApproveSafe} disabled={isTogglingAutoApprove}
                  onChange={handleToggleAutoApprove} label="Auto-approve safe changes" />
              </div>
            </Card>
          )}

          {items === null ? (
            <div className="flex items-center justify-center py-16">
              <Loader2 className="w-5 h-5 text-content-muted animate-spin" aria-label="Loading change requests" />
            </div>
          ) : error ? (
            <Callout tone="danger">{error}</Callout>
          ) : items.length === 0 ? (
            <Card>
              <EmptyState icon={GitPullRequest} title="No change requests yet"
                description="Open the diagram and press “Request Review” to propose your current schema for approval."
                action={<Button variant="primary" icon={Boxes} onClick={() => router.push('/canvas')}>Go to the diagram</Button>} />
            </Card>
          ) : (
            <>
              <div role="group" aria-label="Filter by status" className="flex flex-wrap items-center gap-2">
                {(['all', 'PendingReview', 'Approved', 'Rejected'] as Filter[]).map(f => (
                  <button
                    key={f}
                    type="button"
                    aria-pressed={filter === f}
                    onClick={() => setFilter(f)}
                    className={`h-8 px-3 rounded-full text-[12px] font-medium transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-focus-ring)] ${
                      filter === f
                        ? 'bg-accent-subtle text-accent-text'
                        : 'bg-surface-800 border border-content-primary/10 text-content-muted hover:text-content-primary'
                    }`}
                  >
                    {f === 'all' ? 'All' : STATUS[f].label} <span className="font-mono opacity-70">{counts[f]}</span>
                  </button>
                ))}
              </div>

              {visible.length === 0 ? (
                <Card><EmptyState icon={Clock} title="Nothing here" description="No change requests match this filter." /></Card>
              ) : (
                <ul className="space-y-3">
                  {visible.map(cr => {
                    const risk = RISK[cr.riskLevel];
                    const status = STATUS[cr.status] ?? { label: cr.status, tone: 'neutral' as const, icon: Clock };
                    return (
                      <li key={cr.id}>
                        <button
                          type="button"
                          onClick={() => router.push(`/review/${cr.id}`)}
                          className="group w-full text-left rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 p-4 transition-colors hover:border-content-primary/25 hover:bg-surface-700 cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-focus-ring)]"
                        >
                          <div className="flex items-start justify-between gap-4">
                            <div className="min-w-0">
                              <p className="text-sm font-semibold text-content-primary truncate">{cr.title || 'Schema update'}</p>
                              <p className="mt-1 text-[12px] text-content-muted">
                                {cr.branchName} · {cr.tableCount} tables · {new Date(cr.createdAt).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' })}
                              </p>
                            </div>
                            <ArrowRight className="w-4 h-4 mt-0.5 shrink-0 text-content-subtle group-hover:text-content-primary transition-colors" aria-hidden="true" />
                          </div>
                          <div className="mt-3 flex flex-wrap items-center gap-2">
                            <Badge tone={risk.tone} icon={risk.icon}>{risk.label}</Badge>
                            <Badge tone={status.tone} icon={status.icon}>{status.label}</Badge>
                            {cr.status === 'PendingReview' && (
                              <span className="text-[12px] text-content-muted font-mono">
                                {cr.approvedCount}/{cr.requiredApprovals} approvals
                              </span>
                            )}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </>
          )}
        </div>
      )}

      {tab === 'team' && (
        <div id="tabpanel-team" role="tabpanel" aria-labelledby="tab-team">
          <TeamPanel projectId={activeProjectId} currentUserEmail={currentUserEmail} />
        </div>
      )}

      {tab === 'keys' && (
        <div id="tabpanel-keys" role="tabpanel" aria-labelledby="tab-keys">
          <GatewayKeyPanel projectId={activeProjectId} />
        </div>
      )}
    </PageShell>
  );
}
