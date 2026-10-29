'use client';

import React, { useState, useEffect, useRef, useMemo } from 'react';
import {
  CircleDot,
  CheckCircle2,
  Plus,
  MessageSquare,
  Tag,
  Table as TableIcon,
  X,
  Loader2,
  Send,
  AlertCircle,
  ChevronDown,
  Check,
  Search,
} from 'lucide-react';
import type { HubRepoDetail, HubIssue, HubIssueComment } from '../../types/hub';
import { hubService } from '../../services/hubService';
import { useAuthStore } from '../../store/useAuthStore';
import { useToastStore } from '../../store/useToastStore';
import { Select } from '../ui/Select';

interface HubIssuesTabProps {
  repo: HubRepoDetail;
  onIssueCountChanged?: (openCount: number, closedCount: number) => void;
}

export function HubIssuesTab({ repo, onIssueCountChanged }: HubIssuesTabProps) {
  const { isAuthenticated, user } = useAuthStore();
  const { showToast } = useToastStore();

  const [statusFilter, setStatusFilter] = useState<'open' | 'closed' | 'all'>('open');
  const [tableFilter, setTableFilter] = useState<string>('all');
  const [issues, setIssues] = useState<HubIssue[]>([]);
  const [openCount, setOpenCount] = useState(repo.stats.openIssuesCount);
  const [closedCount, setClosedCount] = useState(repo.stats.closedIssuesCount);
  const [loading, setLoading] = useState(true);

  // New Issue Modal
  const [isNewIssueModalOpen, setIsNewIssueModalOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newBody, setNewBody] = useState('');
  const [newRelatedTable, setNewRelatedTable] = useState('');
  const [newLabels, setNewLabels] = useState('');
  const [isSubmittingIssue, setIsSubmittingIssue] = useState(false);

  // Issue Detail Modal / Drawer
  const [activeIssue, setActiveIssue] = useState<HubIssue | null>(null);
  const [comments, setComments] = useState<HubIssueComment[]>([]);
  const [loadingComments, setLoadingComments] = useState(false);
  const [newCommentBody, setNewCommentBody] = useState('');
  const [isSubmittingComment, setIsSubmittingComment] = useState(false);
  const [isUpdatingState, setIsUpdatingState] = useState(false);
  const [isTableDropdownOpen, setIsTableDropdownOpen] = useState(false);
  const tableDropdownRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (tableDropdownRef.current && !tableDropdownRef.current.contains(event.target as Node)) {
        setIsTableDropdownOpen(false);
      }
    }
    if (isTableDropdownOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [isTableDropdownOpen]);

  const tables = repo.schema?.tables || [];

  const loadIssues = async () => {
    setLoading(true);
    try {
      const res = await hubService.listIssues(repo.id, {
        status: statusFilter,
        relatedTable: tableFilter !== 'all' ? tableFilter : undefined,
      }, { localDraft: repo.isLocalDraft });
      setIssues(res.items);
      setOpenCount(res.openCount);
      setClosedCount(res.closedCount);
      onIssueCountChanged?.(res.openCount, res.closedCount);
    } catch {
      showToast('Failed to load issues.', 'error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadIssues();
  }, [repo.id, statusFilter, tableFilter]);

  const handleCreateIssue = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isAuthenticated) {
      showToast('Please log in to open an issue.', 'warning');
      return;
    }
    if (!newTitle.trim()) return;

    setIsSubmittingIssue(true);
    try {
      const created = await hubService.createIssue(repo.id, {
        title: newTitle.trim(),
        body: newBody.trim(),
        relatedTableName: newRelatedTable || undefined,
        labels: newLabels.trim() || undefined,
      }, { localDraft: repo.isLocalDraft });
      showToast(`Created issue #${created.issueNumber}`, 'success');
      setIsNewIssueModalOpen(false);
      setNewTitle('');
      setNewBody('');
      setNewRelatedTable('');
      setNewLabels('');
      loadIssues();
    } catch {
      showToast('Failed to create issue.', 'error');
    } finally {
      setIsSubmittingIssue(false);
    }
  };

  const handleOpenIssueDetail = async (issue: HubIssue) => {
    setActiveIssue(issue);
    setLoadingComments(true);
    try {
      const comms = await hubService.listComments(issue.id);
      setComments(comms);
    } catch {
      showToast('Could not load comments.', 'error');
    } finally {
      setLoadingComments(false);
    }
  };

  const handleAddComment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isAuthenticated) {
      showToast('Please log in to comment.', 'warning');
      return;
    }
    if (!activeIssue || !newCommentBody.trim()) return;

    setIsSubmittingComment(true);
    try {
      const comm = await hubService.createComment(activeIssue.id, newCommentBody.trim());
      setComments(prev => [...prev, comm]);
      setNewCommentBody('');
      showToast('Comment posted', 'success');
      // Update comment count on issue in list
      setIssues(prev =>
        prev.map(i => (i.id === activeIssue.id ? { ...i, commentsCount: i.commentsCount + 1 } : i))
      );
    } catch {
      showToast('Failed to post comment.', 'error');
    } finally {
      setIsSubmittingComment(false);
    }
  };

  const handleToggleIssueState = async () => {
    if (!activeIssue) return;
    setIsUpdatingState(true);
    const nextState = !activeIssue.isClosed;
    try {
      const res = await hubService.updateIssueState(activeIssue.id, nextState, { localDraft: repo.isLocalDraft });
      setActiveIssue(prev => (prev ? { ...prev, isClosed: res.isClosed, closedAt: res.closedAt } : null));
      showToast(nextState ? 'Issue closed' : 'Issue reopened', 'info');
      loadIssues();
    } catch {
      showToast('Failed to update issue status.', 'error');
    } finally {
      setIsUpdatingState(false);
    }
  };

  const [searchQuery, setSearchQuery] = useState('');

  // Filter issues by status, table, and search query
  const filteredIssues = useMemo(() => {
    return issues.filter((issue) => {
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchTitle = issue.title.toLowerCase().includes(q);
        const matchTable = issue.relatedTableName?.toLowerCase().includes(q);
        const matchLabels = issue.labels?.toLowerCase().includes(q);
        const matchAuthor = issue.createdBy?.username?.toLowerCase().includes(q);
        const matchNumber = String(issue.issueNumber).includes(q);
        if (!matchTitle && !matchTable && !matchLabels && !matchAuthor && !matchNumber) return false;
      }
      return true;
    });
  }, [issues, searchQuery]);

  return (
    <div className="space-y-4 w-full font-sans">
      {/* Top Search & Actions Bar (GitHub Style) */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex items-center flex-wrap gap-2.5 flex-1 min-w-0">
          {/* GitHub-style search input */}
          <div className="relative flex-1 min-w-[220px] max-w-md">
            <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-content-muted" />
            <input
              type="text"
              placeholder="Search all issues or filter by keyword..."
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              className="w-full pl-8 pr-3 py-1.5 text-xs font-mono rounded-[var(--radius-control)] bg-surface-800 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-accent"
            />
          </div>

          {/* Table filter Custom Dropdown */}
          {tables.length > 0 && (
            <div className="relative" ref={tableDropdownRef}>
              <button
                type="button"
                onClick={() => setIsTableDropdownOpen(prev => !prev)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-750 border border-line text-xs font-mono text-content-primary transition-all cursor-pointer shadow-xs"
              >
                <TableIcon className="w-3.5 h-3.5 text-accent-text" />
                <span className="font-medium truncate max-w-[140px]">
                  {tableFilter === 'all' ? 'All Tables' : tableFilter}
                </span>
                <ChevronDown
                  className={`w-3.5 h-3.5 text-content-muted transition-transform duration-150 ${
                    isTableDropdownOpen ? 'rotate-180 text-content-primary' : ''
                  }`}
                />
              </button>

              {isTableDropdownOpen && (
                <div className="absolute left-0 top-full mt-1.5 z-40 w-56 max-h-64 overflow-y-auto rounded-[var(--radius-control)] bg-surface-800 border border-line shadow-2xl py-1 text-xs font-mono divide-y divide-line/30">
                  <button
                    type="button"
                    onClick={() => {
                      setTableFilter('all');
                      setIsTableDropdownOpen(false);
                    }}
                    className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                      tableFilter === 'all'
                        ? 'bg-surface-700 text-content-primary font-semibold'
                        : 'text-content-secondary hover:text-content-primary hover:bg-surface-750'
                    }`}
                  >
                    <span>All Tables</span>
                    {tableFilter === 'all' && <Check className="w-3.5 h-3.5 text-accent-text" />}
                  </button>
                  {tables.map(t => (
                    <button
                      key={t.id}
                      type="button"
                      onClick={() => {
                        setTableFilter(t.name);
                        setIsTableDropdownOpen(false);
                      }}
                      className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                        tableFilter === t.name
                          ? 'bg-surface-700 text-content-primary font-semibold'
                          : 'text-content-secondary hover:text-content-primary hover:bg-surface-750'
                      }`}
                    >
                      <span className="truncate">{t.name}</span>
                      {tableFilter === t.name && <Check className="w-3.5 h-3.5 text-accent-text" />}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>

        {/* New Issue Button */}
        <button
          type="button"
          onClick={() => {
            if (!isAuthenticated) {
              showToast('Please log in to open an issue.', 'warning');
              return;
            }
            setIsNewIssueModalOpen(true);
          }}
          className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer self-start sm:self-auto shrink-0"
        >
          <Plus className="w-3.5 h-3.5 text-accent-text" />
          <span>New Issue</span>
        </button>
      </div>

      {/* GitHub-style Issues List Container */}
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
              <CircleDot className={`w-3.5 h-3.5 ${statusFilter === 'open' ? 'text-accent-text' : 'text-content-muted'}`} />
              <span>{openCount} Open</span>
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
              <span>{closedCount} Closed</span>
            </button>
          </div>

          <span className="text-[11px] font-mono text-content-muted">
            {filteredIssues.length} of {issues.length} issues
          </span>
        </div>

        {/* Issue Rows */}
        <div className="divide-y divide-line">
          {loading ? (
            <div className="flex items-center justify-center p-12 text-content-muted text-xs gap-2">
              <Loader2 className="w-4 h-4 animate-spin text-accent-text" />
              <span>Loading issues...</span>
            </div>
          ) : filteredIssues.length === 0 ? (
            <div className="text-center py-12 px-4 space-y-2">
              <CircleDot className="w-8 h-8 text-content-muted mx-auto" />
              <h4 className="text-sm font-semibold text-content-primary">No issues found</h4>
              <p className="text-xs text-content-muted max-w-sm mx-auto">
                {searchQuery
                  ? `No issues match the search query "${searchQuery}".`
                  : statusFilter === 'open'
                  ? 'There are no open issues for this schema. Click "New Issue" to discuss architecture or report a risk.'
                  : 'There are no closed issues matching this filter.'}
              </p>
            </div>
          ) : (
            filteredIssues.map(issue => (
              <div
                key={issue.id}
                onClick={() => handleOpenIssueDetail(issue)}
                className="flex items-start justify-between gap-4 px-4 py-3 hover:bg-surface-800/60 transition-colors cursor-pointer group"
              >
                <div className="flex items-start gap-3 min-w-0">
                  {issue.isClosed ? (
                    <CheckCircle2 className="w-4 h-4 text-content-muted mt-0.5 shrink-0" />
                  ) : (
                    <CircleDot className="w-4 h-4 text-accent-text mt-0.5 shrink-0" />
                  )}
                  <div className="space-y-1 min-w-0">
                    <div className="flex items-center flex-wrap gap-2">
                      <span className="font-semibold text-sm text-content-primary group-hover:text-accent-text transition-colors truncate">
                        {issue.title}
                      </span>
                      {issue.relatedTableName && (
                        <span className="inline-flex items-center gap-1 text-[10px] font-mono px-2 py-0.5 rounded-full bg-surface-750 text-content-secondary border border-line">
                          <TableIcon className="w-2.5 h-2.5" />
                          {issue.relatedTableName}
                        </span>
                      )}
                      {issue.nslRuleCode && (
                        <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-surface-750 text-accent-text border border-line">
                          {issue.nslRuleCode}
                        </span>
                      )}
                      {issue.labels &&
                        issue.labels.split(',').map((lbl, idx) => (
                          <span
                            key={idx}
                            className="text-[10px] px-2 py-0.5 rounded-full bg-surface-750 text-content-muted border border-line"
                          >
                            {lbl.trim()}
                          </span>
                        ))}
                    </div>

                    <p className="text-xs text-content-muted font-mono">
                      #{issue.issueNumber} opened{' '}
                      {new Date(issue.createdAt).toLocaleDateString()} by {issue.createdBy.username}
                    </p>
                  </div>
                </div>

                {issue.commentsCount > 0 && (
                  <div className="flex items-center gap-1 text-xs text-content-muted shrink-0 pt-0.5">
                    <MessageSquare className="w-3.5 h-3.5" />
                    <span>{issue.commentsCount}</span>
                  </div>
                )}
              </div>
            ))
          )}
        </div>
      </div>

      {/* New Issue Modal */}
      {isNewIssueModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-scrim/80 backdrop-blur-xs">
          <div className="w-full max-w-lg bg-surface-800 border border-line-strong rounded-[var(--radius-card)] p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="text-base font-semibold text-ink-primary flex items-center gap-2">
                <CircleDot className="w-4 h-4 text-accent-text" /> Open New Issue
              </h3>
              <button
                onClick={() => setIsNewIssueModalOpen(false)}
                className="text-ink-muted hover:text-ink-primary"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <form onSubmit={handleCreateIssue} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  Title <span className="text-accent-text">*</span>
                </label>
                <input
                  type="text"
                  placeholder="e.g. Missing composite index on order_items"
                  value={newTitle}
                  onChange={e => setNewTitle(e.target.value)}
                  required
                  className="w-full px-3 py-2 text-sm rounded-[var(--radius-control)] bg-surface-900 border border-line text-ink-primary placeholder:text-ink-muted focus:outline-hidden focus:border-accent"
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-medium text-ink-secondary mb-1">
                    Related Table
                  </label>
                  <Select
                    value={newRelatedTable}
                    onChange={e => setNewRelatedTable(e.target.value)}
                    options={[
                      { value: '', label: 'None / General' },
                      ...tables.map(t => ({ value: t.name, label: t.name })),
                    ]}
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-ink-secondary mb-1">
                    Labels (comma separated)
                  </label>
                  <input
                    type="text"
                    placeholder="e.g. dba-risk, performance"
                    value={newLabels}
                    onChange={e => setNewLabels(e.target.value)}
                    className="w-full px-3 py-2 text-xs rounded-[var(--radius-control)] bg-surface-900 border border-line text-ink-primary placeholder:text-ink-muted focus:outline-hidden focus:border-accent"
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  Description
                </label>
                <textarea
                  rows={4}
                  placeholder="Describe the schema defect, normalization issue, or indexing recommendation..."
                  value={newBody}
                  onChange={e => setNewBody(e.target.value)}
                  className="w-full px-3 py-2 text-xs font-mono rounded-[var(--radius-control)] bg-surface-900 border border-line text-ink-primary placeholder:text-ink-muted focus:outline-hidden focus:border-accent resize-y"
                />
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setIsNewIssueModalOpen(false)}
                  className="px-3 py-1.5 text-xs rounded-[var(--radius-control)] border border-line bg-surface-700 text-ink-secondary hover:text-ink-primary"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingIssue || !newTitle.trim()}
                  className="inline-flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong disabled:opacity-50 cursor-pointer shadow-xs"
                >
                  {isSubmittingIssue ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : null}
                  <span>Submit Issue</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Issue Detail View Modal */}
      {activeIssue && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-scrim/80 backdrop-blur-xs">
          <div className="w-full max-w-2xl max-h-[85vh] bg-surface-800 border border-line-strong rounded-[var(--radius-card)] flex flex-col shadow-2xl overflow-hidden">
            {/* Header */}
            <div className="p-4 border-b border-line bg-surface-850 flex items-start justify-between gap-3">
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <h3 className="text-base font-semibold text-ink-primary">
                    {activeIssue.title}
                  </h3>
                  <span className="text-ink-muted text-sm font-mono">
                    #{activeIssue.issueNumber}
                  </span>
                </div>
                <div className="flex items-center flex-wrap gap-2 text-xs text-ink-muted">
                  <span
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium border ${
                      activeIssue.isClosed
                        ? 'bg-surface-700 text-ink-muted border-line'
                        : 'bg-accent/20 text-accent-text border-accent/30'
                    }`}
                  >
                    {activeIssue.isClosed ? 'Closed' : 'Open'}
                  </span>
                  <span>opened by {activeIssue.createdBy.username}</span>
                  <span>· {new Date(activeIssue.createdAt).toLocaleString()}</span>
                  {activeIssue.relatedTableName && (
                    <span className="font-mono text-ink-secondary">
                      table: {activeIssue.relatedTableName}
                    </span>
                  )}
                </div>
              </div>

              <div className="flex items-center gap-2">
                {/* Close/Reopen button */}
                <button
                  onClick={handleToggleIssueState}
                  disabled={isUpdatingState}
                  className="px-2.5 py-1 text-xs rounded-[var(--radius-control)] border border-line bg-surface-750 text-ink-secondary hover:text-ink-primary transition-all cursor-pointer"
                >
                  {isUpdatingState ? (
                    <Loader2 className="w-3 h-3 animate-spin" />
                  ) : activeIssue.isClosed ? (
                    'Reopen'
                  ) : (
                    'Close Issue'
                  )}
                </button>
                <button
                  onClick={() => setActiveIssue(null)}
                  className="text-ink-muted hover:text-ink-primary p-1"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>

            {/* Content & Comments List */}
            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {/* Issue Description */}
              {activeIssue.body && (
                <div className="p-3.5 rounded-[var(--radius-control)] bg-surface-900 border border-line text-xs font-mono text-ink-primary whitespace-pre-wrap leading-relaxed">
                  {activeIssue.body}
                </div>
              )}

              {/* Comments Thread */}
              <div className="space-y-3">
                <h4 className="text-xs font-semibold text-ink-muted uppercase font-mono tracking-wider">
                  Discussion ({comments.length})
                </h4>

                {loadingComments ? (
                  <div className="flex items-center justify-center py-6 text-xs text-ink-muted gap-2">
                    <Loader2 className="w-3.5 h-3.5 animate-spin text-accent-text" />
                    <span>Loading discussion...</span>
                  </div>
                ) : comments.length === 0 ? (
                  <p className="text-xs text-ink-muted italic py-2">
                    No comments yet. Leave a comment below.
                  </p>
                ) : (
                  comments.map(c => (
                    <div
                      key={c.id}
                      className="p-3 rounded-[var(--radius-control)] bg-surface-850 border border-line space-y-1.5"
                    >
                      <div className="flex items-center justify-between text-xs text-ink-muted font-mono">
                        <span className="font-semibold text-ink-secondary">{c.author.username}</span>
                        <span>{new Date(c.createdAt).toLocaleTimeString()}</span>
                      </div>
                      <p className="text-xs text-ink-primary font-mono whitespace-pre-wrap">
                        {c.body}
                      </p>
                    </div>
                  ))
                )}
              </div>
            </div>

            {/* Comment Form */}
            <form onSubmit={handleAddComment} className="p-4 border-t border-line bg-surface-850">
              <div className="flex items-start gap-2">
                <textarea
                  rows={2}
                  placeholder={
                    isAuthenticated
                      ? 'Leave a comment on this issue...'
                      : 'Log in to comment on this issue'
                  }
                  value={newCommentBody}
                  onChange={e => setNewCommentBody(e.target.value)}
                  disabled={!isAuthenticated || isSubmittingComment}
                  className="flex-1 px-3 py-1.5 text-xs font-mono rounded-[var(--radius-control)] bg-surface-900 border border-line text-ink-primary placeholder:text-ink-muted focus:outline-hidden focus:border-accent disabled:opacity-50"
                />
                <button
                  type="submit"
                  disabled={!isAuthenticated || isSubmittingComment || !newCommentBody.trim()}
                  className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong disabled:opacity-50 transition-all cursor-pointer shrink-0 shadow-xs"
                >
                  {isSubmittingComment ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <Send className="w-3.5 h-3.5" />
                  )}
                  <span>Comment</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
