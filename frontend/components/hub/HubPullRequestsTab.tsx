'use client';

import React, { useState, useEffect, useMemo, useCallback } from 'react';
import Link from 'next/link';
import {
  GitPullRequest,
  CheckCircle2,
  AlertTriangle,
  Flame,
  ShieldCheck,
  ExternalLink,
  Loader2,
  GitBranch,
  Search,
  Plus,
  X,
  ArrowRight,
  Sparkles,
} from 'lucide-react';
import type { HubRepoDetail } from '../../types/hub';
import type { ChangeRequestSummary } from '../../types/changeRequest';
import type { DatabaseSchema } from '../../types/schema';
import { changeRequestService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';

interface HubPullRequestsTabProps {
  repo: HubRepoDetail;
}

export function HubPullRequestsTab({ repo }: HubPullRequestsTabProps) {
  const { showToast } = useToastStore();
  const [prs, setPrs] = useState<ChangeRequestSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<'open' | 'closed' | 'all'>('open');
  const [searchQuery, setSearchQuery] = useState('');

  const loadPrs = useCallback(async () => {
    setLoading(true);
    try {
      const list = await changeRequestService.listForProject(repo.id);
      setPrs(list);
    } catch {
      showToast('Failed to load pull requests.', 'error');
    } finally {
      setLoading(false);
    }
  }, [repo.id, showToast]);

  useEffect(() => {
    loadPrs();
  }, [loadPrs]);

  // Branch list
  const branches = useMemo(() => {
    const list = repo.branches || [];
    if (list.length === 0) {
      return [{ id: 'main', name: 'main', isDefault: true, createdAt: '' }];
    }
    return list;
  }, [repo.branches]);

  // New PR Modal State
  const [isNewPrModalOpen, setIsNewPrModalOpen] = useState(false);
  const [sourceBranch, setSourceBranch] = useState(branches.find(b => !b.isDefault)?.name || 'feature/schema-update');
  const [targetBranch, setTargetBranch] = useState(branches.find(b => b.isDefault)?.name || 'main');
  const [prTitle, setPrTitle] = useState('');
  const [prMessage, setPrMessage] = useState('');
  const [isSubmittingPr, setIsSubmittingPr] = useState(false);

  const handleOpenNewPrModal = () => {
    const branchName = sourceBranch || 'feature/schema-update';
    setPrTitle(`feat(${branchName}): Propose schema changes for ${repo.name}`);
    setPrMessage('Automated migration verification requested before applying DDL changes to target environment.');
    setIsNewPrModalOpen(true);
  };

  const handleCreatePr = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!prTitle.trim()) {
      showToast('Please provide a title for the pull request.', 'warning');
      return;
    }

    setIsSubmittingPr(true);
    try {
      const activeSchema = useSchemaStore.getState().schema;
      const projectSnap = useProjectHistoryStore.getState().projects.find(p => p.id === repo.id);
      const schemaToSubmit: DatabaseSchema = projectSnap?.schema || activeSchema || {
        schemaId: repo.id,
        name: repo.name,
        tables: [],
        relations: [],
      };

      const created = await changeRequestService.createQuick(
        repo.id,
        schemaToSubmit,
        prTitle.trim(),
        prMessage.trim() || undefined
      );

      showToast(`Pull Request #${created.id.slice(0, 8)} opened successfully!`, 'success');
      setIsNewPrModalOpen(false);
      await loadPrs();
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Failed to create pull request.', 'error');
    } finally {
      setIsSubmittingPr(false);
    }
  };

  const openPrs = prs.filter(p => p.status === 'PendingReview');
  const closedPrs = prs.filter(p => p.status === 'Approved' || p.status === 'Rejected');

  const filteredPrs = useMemo(() => {
    const base = statusFilter === 'open' ? openPrs : statusFilter === 'closed' ? closedPrs : prs;
    if (!searchQuery.trim()) return base;
    const q = searchQuery.toLowerCase().trim();
    return base.filter(
      p =>
        (p.title && p.title.toLowerCase().includes(q)) ||
        p.branchName?.toLowerCase().includes(q) ||
        p.riskLevel?.toLowerCase().includes(q)
    );
  }, [statusFilter, openPrs, closedPrs, prs, searchQuery]);

  const renderRiskBadge = (risk: string) => {
    switch (risk) {
      case 'Breaking':
        return (
          <span className="inline-flex items-center gap-1 text-[10px] font-mono px-2 py-0.5 rounded-full bg-danger-subtle text-danger border border-danger/30">
            <Flame className="w-3 h-3" /> Breaking
          </span>
        );
      case 'Risky':
        return (
          <span className="inline-flex items-center gap-1 text-[10px] font-mono px-2 py-0.5 rounded-full bg-surface-700 text-content-secondary border border-line">
            <AlertTriangle className="w-3 h-3 text-content-muted" /> Risky
          </span>
        );
      case 'Safe':
      default:
        return (
          <span className="inline-flex items-center gap-1 text-[10px] font-mono px-2 py-0.5 rounded-full bg-surface-700 text-content-secondary border border-line">
            <ShieldCheck className="w-3 h-3 text-accent-text" /> Safe
          </span>
        );
    }
  };

  return (
    <div className="space-y-4 w-full font-sans">
      {/* Top Search & Actions Bar (GitHub Style) */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="relative flex-1 min-w-[220px] max-w-md">
          <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-content-muted" />
          <input
            type="text"
            placeholder="Search pull requests or filter by branch..."
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
            className="w-full pl-8 pr-3 py-1.5 text-xs font-mono rounded-[var(--radius-control)] bg-surface-800 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-accent"
          />
        </div>

        <button
          type="button"
          onClick={handleOpenNewPrModal}
          className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer self-start sm:self-auto shrink-0"
        >
          <GitPullRequest className="w-3.5 h-3.5 text-accent-text" />
          <span>New Pull Request</span>
        </button>
      </div>

      {/* GitHub-style PRs List Table */}
      <div className="bg-surface-850 rounded-[var(--radius-card)] border border-line overflow-hidden shadow-xs">
        {/* Table Header Row (GitHub Style) */}
        <div className="px-4 py-2.5 bg-surface-800 border-b border-line flex items-center justify-between text-xs">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => setStatusFilter('open')}
              className={`inline-flex items-center gap-1.5 transition-colors cursor-pointer ${
                statusFilter === 'open'
                  ? 'text-content-primary font-semibold'
                  : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              <GitPullRequest className={`w-3.5 h-3.5 ${statusFilter === 'open' ? 'text-accent-text' : 'text-content-muted'}`} />
              <span>{openPrs.length} Open</span>
            </button>

            <button
              type="button"
              onClick={() => setStatusFilter('closed')}
              className={`inline-flex items-center gap-1.5 transition-colors cursor-pointer ${
                statusFilter === 'closed'
                  ? 'text-content-primary font-semibold'
                  : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              <CheckCircle2 className={`w-3.5 h-3.5 ${statusFilter === 'closed' ? 'text-accent-text' : 'text-content-muted'}`} />
              <span>{closedPrs.length} Closed</span>
            </button>
          </div>

          <span className="text-[11px] font-mono text-content-muted">
            {filteredPrs.length} pull requests
          </span>
        </div>

        {/* PRs List Rows */}
        <div className="divide-y divide-line">
          {loading ? (
            <div className="flex items-center justify-center p-12 text-content-muted text-xs gap-2">
              <Loader2 className="w-4 h-4 animate-spin text-accent-text" />
              <span>Loading pull requests...</span>
            </div>
          ) : filteredPrs.length === 0 ? (
            <div className="text-center py-12 px-4 space-y-2">
              <GitPullRequest className="w-8 h-8 text-content-muted mx-auto" />
              <h4 className="text-sm font-semibold text-content-primary">No pull requests found</h4>
              <p className="text-xs text-content-muted max-w-sm mx-auto">
                {searchQuery
                  ? `No pull requests match the query "${searchQuery}".`
                  : `There are no ${statusFilter} database change reviews for this repository.`}
              </p>
            </div>
          ) : (
            filteredPrs.map(pr => (
              <Link
                key={pr.id}
                href={`/review/${pr.id}`}
                className="flex items-start justify-between gap-4 px-4 py-3 hover:bg-surface-800/60 transition-colors block group"
              >
                <div className="flex items-start gap-3 min-w-0">
                  <GitPullRequest
                    className={`w-4 h-4 mt-0.5 shrink-0 ${
                      pr.status === 'Approved'
                        ? 'text-accent-text'
                        : pr.status === 'Rejected'
                        ? 'text-danger'
                        : 'text-content-secondary'
                    }`}
                  />

                  <div className="space-y-1 min-w-0">
                    <div className="flex items-center flex-wrap gap-2">
                      <span className="font-semibold text-sm text-content-primary group-hover:text-accent-text transition-colors truncate">
                        {pr.title}
                      </span>
                      {renderRiskBadge(pr.riskLevel)}
                      <span className="inline-flex items-center gap-1 text-[10px] font-mono px-2 py-0.5 rounded-full bg-surface-750 text-content-secondary border border-line">
                        <GitBranch className="w-2.5 h-2.5 text-accent-text" />
                        {pr.branchName || 'main'}
                      </span>
                    </div>

                    <p className="text-xs text-content-muted font-mono">
                      #{pr.id.slice(0, 8)} opened {new Date(pr.createdAt).toLocaleDateString()} ·{' '}
                      {pr.approvedCount}/{pr.requiredApprovals} approvals · {pr.tableCount} tables affected
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-1 text-xs text-content-muted group-hover:text-content-primary shrink-0 transition-colors pt-0.5">
                  <span>View Review</span>
                  <ExternalLink className="w-3.5 h-3.5" />
                </div>
              </Link>
            ))
          )}
        </div>
      </div>

      {/* GitHub-style New Pull Request Modal */}
      {isNewPrModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-surface-950/85 backdrop-blur-md animate-in fade-in duration-150">
          <div className="w-full max-w-xl rounded-[var(--radius-card)] bg-surface-900 border border-line-strong shadow-2xl p-6 space-y-5 text-content-primary">
            {/* Modal Header */}
            <div className="flex items-center justify-between pb-3 border-b border-line">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-800 border border-line-strong flex items-center justify-center text-accent-text">
                  <GitPullRequest className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="font-bold text-sm text-content-primary">Open a Pull Request</h3>
                  <p className="text-micro text-content-muted">Compare schema changes and request automated risk review.</p>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setIsNewPrModalOpen(false)}
                className="text-content-muted hover:text-content-primary p-1.5 rounded-[var(--radius-control)] hover:bg-surface-800 transition-colors cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Branch Compare Picker Bar */}
            <div className="p-3 rounded-[var(--radius-control)] bg-surface-800 border border-line flex flex-wrap items-center gap-2 text-xs font-mono">
              <div className="flex items-center gap-1.5">
                <span className="text-content-muted text-micro uppercase font-bold">Base:</span>
                <select
                  value={targetBranch}
                  onChange={e => setTargetBranch(e.target.value)}
                  className="h-7 px-2.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-content-primary text-xs font-semibold cursor-pointer"
                >
                  {branches.map(b => (
                    <option key={b.id} value={b.name} className="bg-surface-850">
                      {b.name} {b.isDefault ? '(default)' : ''}
                    </option>
                  ))}
                </select>
              </div>

              <ArrowRight className="w-3.5 h-3.5 text-content-muted" />

              <div className="flex items-center gap-1.5">
                <span className="text-content-muted text-micro uppercase font-bold">Compare:</span>
                <select
                  value={sourceBranch}
                  onChange={e => setSourceBranch(e.target.value)}
                  className="h-7 px-2.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-content-primary text-xs font-semibold cursor-pointer"
                >
                  {branches.map(b => (
                    <option key={`src-${b.id}`} value={b.name} className="bg-surface-850">
                      {b.name}
                    </option>
                  ))}
                  {!branches.some(b => b.name === 'feature/schema-update') && (
                    <option value="feature/schema-update" className="bg-surface-850">
                      feature/schema-update (active draft)
                    </option>
                  )}
                </select>
              </div>
            </div>

            {/* Form */}
            <form onSubmit={handleCreatePr} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-content-primary mb-1.5">
                  Title
                </label>
                <input
                  type="text"
                  required
                  value={prTitle}
                  onChange={e => setPrTitle(e.target.value)}
                  placeholder="e.g. feat(schema): Add audit logs and partition indexes"
                  className="w-full px-3 py-2 text-xs rounded-[var(--radius-control)] bg-surface-800 border border-line-strong text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-focus-ring"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-content-primary mb-1.5">
                  Description &amp; Migration Intent
                </label>
                <textarea
                  rows={3}
                  value={prMessage}
                  onChange={e => setPrMessage(e.target.value)}
                  placeholder="Explain the purpose of this database migration, expected downtime, and impacted microservices..."
                  className="w-full px-3 py-2 text-xs rounded-[var(--radius-control)] bg-surface-800 border border-line-strong text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-focus-ring resize-none font-mono"
                />
              </div>

              {/* Informative Note */}
              <div className="p-3 rounded-[var(--radius-control)] bg-surface-800/60 border border-line text-[11px] text-content-muted flex items-start gap-2.5">
                <Sparkles className="w-4 h-4 text-accent-text shrink-0 mt-0.5" />
                <span>
                  Creating this pull request runs Namines Schema Impact Analyzer, evaluates migration reversibility, and registers the review on <strong className="text-content-primary">/review</strong> with ephemeral DDL tests.
                </span>
              </div>

              {/* Actions */}
              <div className="flex items-center justify-end gap-2 pt-2 border-t border-line">
                <button
                  type="button"
                  onClick={() => setIsNewPrModalOpen(false)}
                  disabled={isSubmittingPr}
                  className="px-3.5 py-1.5 text-xs font-medium rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-750 text-content-secondary hover:text-content-primary border border-line cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingPr}
                  className="inline-flex items-center gap-1.5 px-4 py-1.5 text-xs font-bold rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-surface-950 shadow-sm transition-all cursor-pointer disabled:opacity-50"
                >
                  {isSubmittingPr ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Creating…</span>
                    </>
                  ) : (
                    <>
                      <GitPullRequest className="w-3.5 h-3.5" />
                      <span>Create Pull Request</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
