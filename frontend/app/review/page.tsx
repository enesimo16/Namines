'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, GitPullRequest, Loader2 } from 'lucide-react';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useAuthStore } from '../../store/useAuthStore';
import { changeRequestService, authService } from '../../services/api';
import { ChangeRequestSummary, RiskLevel } from '../../types/changeRequest';
import { useToastStore } from '../../store/useToastStore';
import TeamPanel from '../../components/review/TeamPanel';
import GatewayKeyPanel from '../../components/review/GatewayKeyPanel';

const RISK_LABEL: Record<RiskLevel, string> = {
  Safe: 'SAFE',
  Risky: 'RISKY',
  Destructive: 'DESTRUCTIVE',
  Breaking: 'BREAKING',
};

const RISK_COLOR: Record<RiskLevel, string> = {
  Safe: 'text-success-text bg-success-text/10',
  Risky: 'text-content-secondary bg-white/[0.08]',
  Destructive: 'text-danger-text bg-danger-text/10',
  Breaking: 'text-danger-text bg-danger-text/15',
};

const STATUS_LABEL: Record<string, string> = {
  PendingReview: 'Pending Review',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

const STATUS_COLOR: Record<string, string> = {
  PendingReview: 'text-content-secondary bg-white/[0.08]',
  Approved: 'text-success-text bg-success-text/10',
  Rejected: 'text-danger-text bg-danger-text/10',
};

export default function ChangeRequestListPage() {
  const router = useRouter();
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const currentUserEmail = useAuthStore(s => s.user?.email ?? null);

  const showToast = useToastStore(s => s.showToast);
  const [items, setItems] = useState<ChangeRequestSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [autoApproveSafe, setAutoApproveSafeState] = useState<boolean | null>(null);
  const [isTogglingAutoApprove, setIsTogglingAutoApprove] = useState(false);

  useEffect(() => {
    if (!isAuthenticated || !activeProjectId) {
      setItems([]);
      return;
    }
    changeRequestService.listForProject(activeProjectId)
      .then(setItems)
      .catch(() => setError('Failed to load change requests.'));

    authService.getCloudProjects()
      .then(projects => {
        const project = projects.find((p: any) => p.id === activeProjectId);
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

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary font-sans">
      <div className="max-w-3xl mx-auto px-6 py-8">
        <button
          onClick={() => router.push('/canvas')}
          className="flex items-center gap-2 text-xs font-semibold text-content-muted hover:text-content-primary transition-colors cursor-pointer mb-6"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back to Diagram</span>
        </button>

        <div className="flex items-center gap-2.5 mb-1">
          <GitPullRequest className="w-5 h-5 text-content-muted" />
          <h1 className="text-lg font-bold text-content-primary">Database Change Requests</h1>
        </div>
        <p className="text-xs text-content-subtle mb-4">
          Review pending schema changes for this project before they're applied.
        </p>

        {isAuthenticated && activeProjectId && autoApproveSafe !== null && (
          <button
            onClick={handleToggleAutoApprove}
            disabled={isTogglingAutoApprove}
            className="w-full flex items-center justify-between gap-3 bg-surface-700 border border-content-primary/15 rounded-xl px-4 py-3 mb-6 text-left cursor-pointer hover:bg-surface-600 transition-colors disabled:opacity-60"
          >
            <div>
              <p className="text-xs font-semibold text-content-secondary">Auto-approve Safe changes</p>
              <p className="text-[10px] text-content-subtle mt-0.5">Safe-risk change requests skip human review and are approved immediately.</p>
            </div>
            <span
              className={`relative shrink-0 w-9 h-5 rounded-full transition-colors ${autoApproveSafe ? 'bg-accent-hover' : 'bg-white/[0.12]'}`}
            >
              <span className={`absolute top-0.5 left-0.5 w-4 h-4 rounded-full bg-content-primary transition-transform ${autoApproveSafe ? 'translate-x-4' : ''}`} />
            </span>
          </button>
        )}

        {isAuthenticated && activeProjectId && (
          <div className="mb-6">
            <TeamPanel projectId={activeProjectId} currentUserEmail={currentUserEmail} />
          </div>
        )}

        {isAuthenticated && activeProjectId && (
          <div className="mb-6">
            <GatewayKeyPanel projectId={activeProjectId} />
          </div>
        )}

        {!isAuthenticated ? (
          <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-8 text-center">
            <p className="text-sm text-content-secondary">Sign in to see change requests for your projects.</p>
          </div>
        ) : !activeProjectId ? (
          <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-8 text-center">
            <p className="text-sm text-content-secondary">No active project. Open a project from the canvas first.</p>
          </div>
        ) : items === null ? (
          <div className="flex items-center justify-center py-16">
            <Loader2 className="w-5 h-5 text-content-muted animate-spin" />
          </div>
        ) : error ? (
          <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-8 text-center text-sm text-danger-text">
            {error}
          </div>
        ) : items.length === 0 ? (
          <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-8 text-center">
            <p className="text-sm text-content-secondary">No change requests yet.</p>
            <p className="text-xs text-content-subtle mt-1">Use "Request Review" on the canvas toolbar to open one.</p>
          </div>
        ) : (
          <div className="flex flex-col gap-2">
            {items.map(cr => (
              <button
                key={cr.id}
                onClick={() => router.push(`/review/${cr.id}`)}
                className="text-left bg-surface-700 hover:bg-surface-600 border border-content-primary/15 rounded-xl p-4 transition-all cursor-pointer"
              >
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p className="text-sm font-semibold text-content-primary truncate">{cr.title || 'Schema update'}</p>
                    <p className="text-[11px] text-content-subtle mt-0.5">
                      {cr.branchName} · {cr.tableCount} tables · {new Date(cr.createdAt).toLocaleString()}
                    </p>
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    <span className={`text-[9px] font-bold uppercase tracking-wider px-2 py-1 rounded-full ${RISK_COLOR[cr.riskLevel]}`}>
                      {RISK_LABEL[cr.riskLevel]}
                    </span>
                    <span className={`text-[9px] font-bold uppercase tracking-wider px-2 py-1 rounded-full ${STATUS_COLOR[cr.status] || 'text-content-subtle bg-white/[0.04]'}`}>
                      {STATUS_LABEL[cr.status] || cr.status}
                    </span>
                  </div>
                </div>
                {cr.status === 'PendingReview' && (
                  <p className="text-[10px] text-content-subtle mt-2">
                    {cr.approvedCount}/{cr.requiredApprovals} approvals
                  </p>
                )}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
