'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import Prism from 'prismjs';
import 'prismjs/components/prism-csharp';
import 'prismjs/components/prism-sql';
import {
  ArrowLeft, Check, X, ShieldCheck, AlertTriangle, PlayCircle, Loader2,
  FileSearch, Upload, History, Terminal, Table2, GitBranch, Sparkles,
  Trash2, Lock, Database, Copy, Boxes, Users, KeyRound, Clock,
  ListFilter, ArrowRight, GitPullRequest, CheckCircle2, XCircle
} from 'lucide-react';
import { changeRequestService, authService } from '../../services/api';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useToastStore } from '../../store/useToastStore';
import {
  ChangeRequestDetail, ChangeRequestSummary,
  ApprovalDecision, AffectedCodeScanResult, ChangeRequestAuditEntry, RiskLevel
} from '../../types/changeRequest';
import TeamPanel from './TeamPanel';
import GatewayKeyPanel from './GatewayKeyPanel';
import { Switch, Select } from '../workspace/WorkspaceKit';
import Skeleton from '../ui/Skeleton';
import { useLocale } from '../../lib/i18n';

const RISK_CONFIG: Record<RiskLevel, { label: string; badgeColor: string; barColor: string; fill: number; tone: 'success' | 'neutral' | 'danger'; icon: typeof ShieldCheck }> = {
  Safe: { label: 'Safe', badgeColor: 'bg-success-text/15 text-success-text border border-success-text/30', barColor: 'bg-success-text', fill: 20, tone: 'success', icon: ShieldCheck },
  Risky: { label: 'Medium Risk', badgeColor: 'bg-accent-subtle text-accent-text border border-accent/30', barColor: 'bg-accent-text', fill: 50, tone: 'neutral', icon: AlertTriangle },
  Destructive: { label: 'High Risk', badgeColor: 'bg-danger-text/15 text-danger-text border border-danger-text/30', barColor: 'bg-danger-text', fill: 80, tone: 'danger', icon: AlertTriangle },
  Breaking: { label: 'Critical Risk', badgeColor: 'bg-danger-text/20 text-danger-text border border-danger-text/40', barColor: 'bg-danger-text', fill: 100, tone: 'danger', icon: AlertTriangle },
};

const STATUS_CONFIG: Record<string, { label: string; tone: 'success' | 'neutral' | 'danger'; icon: typeof Clock }> = {
  PendingReview: { label: 'Pending Review', tone: 'neutral', icon: Clock },
  Approved: { label: 'Approved', tone: 'success', icon: CheckCircle2 },
  Rejected: { label: 'Rejected', tone: 'danger', icon: XCircle },
};

const AUDIT_ACTION_LABEL: Record<string, string> = {
  Created: 'Change request created',
  AutoApproved: 'Auto-approved',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

type MainTab = 'review' | 'all_requests' | 'team' | 'keys';
type ReviewSubTab = 'DIFF' | 'SQL' | 'IMPACT' | 'TESTS' | 'CODE' | 'HISTORY';
type StatusFilter = 'all' | 'PendingReview' | 'Approved' | 'Rejected';

interface UnifiedReviewConsoleProps {
  initialCrId?: string;
}

export default function UnifiedReviewConsole({ initialCrId }: UnifiedReviewConsoleProps) {
  const router = useRouter();
  const showToast = useToastStore(s => s.showToast);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const projectName = useSchemaStore(s => s.projectName);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const currentUserEmail = useAuthStore(s => s.user?.email ?? null);
  const locale = useLocale();
  const returnLabel = locale === 'tr' ? 'Diagrama Geri Dön' : 'Return to Diagram';

  // Main navigation tab
  const [mainTab, setMainTab] = useState<MainTab>(initialCrId ? 'review' : 'all_requests');
  // Review sub-tab
  const [reviewSubTab, setReviewSubTab] = useState<ReviewSubTab>('DIFF');

  // Currently selected CR ID and detail
  const [currentCrId, setCurrentCrId] = useState<string | null>(initialCrId || null);
  const [cr, setCr] = useState<ChangeRequestDetail | null>(null);
  const [auditLog, setAuditLog] = useState<ChangeRequestAuditEntry[]>([]);
  const [loadingCr, setLoadingCr] = useState(false);
  const [crError, setCrError] = useState<string | null>(null);

  // All Change Requests list state
  const [requestsList, setRequestsList] = useState<ChangeRequestSummary[] | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [autoApproveSafe, setAutoApproveSafeState] = useState<boolean | null>(null);
  const [isTogglingAutoApprove, setIsTogglingAutoApprove] = useState(false);

  // Active review interactions
  const [isDeciding, setIsDeciding] = useState(false);
  const [isRunningTests, setIsRunningTests] = useState(false);
  const [searchTable, setSearchTable] = useState('');
  const [copiedCode, setCopiedCode] = useState(false);
  const [codeFiles, setCodeFiles] = useState<{ fileName: string; content: string }[]>([]);
  const [isScanning, setIsScanning] = useState(false);
  const [scanResult, setScanResult] = useState<AffectedCodeScanResult | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const codeRef = useRef<HTMLElement>(null);

  // Load project change requests list
  useEffect(() => {
    const projId = activeProjectId || 'local-project';
    changeRequestService.listForProject(projId)
      .then(items => {
        setRequestsList(items);
        // If no CR was specified in URL, auto-select the first pending (or latest) CR for detail view when switched
        if (!initialCrId && items && items.length > 0) {
          const defaultCr = items.find(i => i.status === 'PendingReview') || items[0];
          setCurrentCrId(defaultCr.id);
        }
      })
      .catch((err) => {
        if (err?.response?.status === 404) {
          setRequestsList([]);
          return;
        }
        setRequestsList([]);
      });

    if (isAuthenticated && activeProjectId) {
      authService.getCloudProjects()
        .then(projects => {
          const project = projects.find(p => p.id === activeProjectId);
          setAutoApproveSafeState(project?.autoApproveSafeChanges ?? false);
        })
        .catch(() => setAutoApproveSafeState(false));
    }
  }, [activeProjectId, isAuthenticated, initialCrId]);

  // Load details whenever currentCrId changes
  useEffect(() => {
    if (!currentCrId) {
      setCr(null);
      return;
    }

    setLoadingCr(true);
    setCrError(null);

    Promise.all([
      changeRequestService.getDetail(currentCrId),
      changeRequestService.getAuditLog(currentCrId).catch(() => [] as ChangeRequestAuditEntry[]),
    ])
      .then(([detail, audit]) => {
        setCr(detail);
        setAuditLog(audit);
      })
      .catch(() => {
        setCrError('Failed to load review details.');
      })
      .finally(() => {
        setLoadingCr(false);
      });
  }, [currentCrId]);

  // Syntax highlight when viewing migration SQL
  useEffect(() => {
    if (reviewSubTab === 'SQL' && codeRef.current && cr?.migration?.upCode) {
      Prism.highlightElement(codeRef.current);
    }
  }, [reviewSubTab, cr?.migration?.upCode]);

  // Toggle auto-approve safe
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

  // Switch to another review seamlessly inside this single console
  const handleSelectCr = (id: string) => {
    setCurrentCrId(id);
    setMainTab('review');
    window.history.pushState({}, '', `/review/${id}`);
  };

  // Decide on active CR
  const handleDecide = async (decision: ApprovalDecision) => {
    if (!cr) return;
    setIsDeciding(true);
    try {
      await changeRequestService.decide(cr.id, decision);
      showToast(`Change request marked as ${decision}`, 'success');

      // Refresh detail, list & audit log
      const [updatedDetail, items, logs] = await Promise.all([
        changeRequestService.getDetail(cr.id).catch(() => null),
        changeRequestService.listForProject(activeProjectId || 'local-project').catch(() => null),
        changeRequestService.getAuditLog(cr.id).catch(() => [] as ChangeRequestAuditEntry[]),
      ]);
      if (updatedDetail) setCr(updatedDetail);
      if (items) setRequestsList(items);
      setAuditLog(logs);
    } catch {
      showToast(`Failed to ${decision.toLowerCase()} change request.`, 'error');
    } finally {
      setIsDeciding(false);
    }
  };

  // Ephemeral container verification test
  const handleRunTests = async () => {
    if (!cr) return;
    setIsRunningTests(true);
    try {
      const result = await changeRequestService.runTests(cr.id);
      const updated = await changeRequestService.getDetail(cr.id).catch(() => null);
      if (updated) setCr(updated);
      showToast(
        result.success
          ? 'Verification tests passed in ephemeral container!'
          : 'Container verification reported issues.',
        result.success ? 'success' : 'error'
      );
    } catch {
      showToast('Ephemeral container test failed to complete.', 'error');
    } finally {
      setIsRunningTests(false);
    }
  };

  // Code scanning
  const handleFilesSelected = (files: FileList | null) => {
    if (!files) return;
    const readList: { fileName: string; content: string }[] = [];
    Array.from(files).forEach(f => {
      const reader = new FileReader();
      reader.onload = e => {
        readList.push({ fileName: f.name, content: (e.target?.result as string) || '' });
        if (readList.length === files.length) {
          setCodeFiles(prev => [...prev, ...readList]);
        }
      };
      reader.readAsText(f);
    });
  };

  const handleScanCode = async () => {
    if (!cr || codeFiles.length === 0) return;
    setIsScanning(true);
    try {
      const result = await changeRequestService.scanAffectedCode(cr.id, codeFiles);
      setScanResult(result);
      showToast(`Scan complete: ${result.matches.length} references found.`, 'info');
    } catch {
      showToast('Static impact scan failed.', 'error');
    } finally {
      setIsScanning(false);
    }
  };

  const copyMigrationSql = () => {
    if (!cr?.migration?.upCode) return;
    navigator.clipboard.writeText(cr.migration.upCode);
    setCopiedCode(true);
    showToast('Migration SQL copied to clipboard', 'success');
    setTimeout(() => setCopiedCode(false), 2000);
  };

  // Computed data for active CR
  const filteredTables = useMemo(() => {
    if (!cr?.impact?.affectedTables) return [];
    if (!searchTable.trim()) return cr.impact.affectedTables;
    const q = searchTable.toLowerCase();
    return cr.impact.affectedTables.filter(t =>
      t.tableName.toLowerCase().includes(q) ||
      t.changedColumns.some(c => c.toLowerCase().includes(q))
    );
  }, [cr, searchTable]);

  // Filtered requests list
  const filteredRequests = useMemo(() => {
    if (!requestsList) return [];
    if (statusFilter === 'all') return requestsList;
    return requestsList.filter(r => r.status === statusFilter);
  }, [requestsList, statusFilter]);

  const requestCounts = useMemo(() => {
    const list = requestsList || [];
    return {
      all: list.length,
      PendingReview: list.filter(r => r.status === 'PendingReview').length,
      Approved: list.filter(r => r.status === 'Approved').length,
      Rejected: list.filter(r => r.status === 'Rejected').length,
    };
  }, [requestsList]);

  const impact = cr?.impact;
  const meter = cr ? RISK_CONFIG[cr.riskLevel] : RISK_CONFIG.Safe;
  const breakingCount = impact?.breakingChanges.length ?? 0;
  const affectedTableCount = impact?.affectedTables.length ?? 0;

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary font-sans py-5 px-3 sm:px-6">
      
      {/* ── Tek Ortak Shopify Polaris Konsolu (Unified Single Console) ──────── */}
      <div className="max-w-6xl mx-auto flex flex-col gap-4">

        {/* 1. Üst Navigasyon & Diagram'a Geri Dön Butonu ───────────────────────── */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <button
            onClick={() => router.push('/canvas')}
            className="group inline-flex items-center gap-2 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 text-content-secondary hover:text-content-primary border border-line text-xs font-semibold transition-all cursor-pointer shadow-xs"
            title={returnLabel}
            aria-label={returnLabel}
          >
            <ArrowLeft className="w-3.5 h-3.5 transition-transform group-hover:-translate-x-0.5 text-namines-teal-vibrant" />
            <span>{returnLabel}</span>
          </button>
        </div>

        {/* 2. Tek Ortak Panel Çerçevesi (Unified Card) ────────────────────────── */}
        <div className="bg-surface-800 border border-line rounded-[var(--radius-modal)] shadow-xl overflow-hidden flex flex-col">

          {/* Panel Header (Shopify Polaris Page Header) ───────────────────────── */}
          <div className="p-4 sm:p-5 border-b border-line bg-surface-850/80 flex flex-col md:flex-row md:items-center justify-between gap-4">
            <div className="min-w-0 space-y-1">
              <div className="flex flex-wrap items-center gap-2.5">
                <h1 className="text-base sm:text-lg font-bold text-content-primary tracking-tight truncate">
                  {cr ? (cr.title || `${projectName || 'Project'} Change Review`) : `${projectName || 'Project'} Change Reviews`}
                </h1>
                {cr && (
                  <>
                    <span className={`text-[10px] font-bold px-2.5 py-0.5 rounded-full uppercase tracking-wider shrink-0 ${
                      cr.status === 'Approved' ? 'bg-success-text/15 text-success-text border border-success-text/25'
                        : cr.status === 'Rejected' ? 'bg-danger-text/15 text-danger-text border border-danger-text/25'
                        : 'bg-accent-subtle text-accent-text border border-accent/25'
                    }`}>
                      {cr.status === 'PendingReview' ? 'Pending Review' : cr.status}
                    </span>
                    <span className={`text-[10px] font-bold px-2 py-0.5 rounded-full uppercase tracking-wider shrink-0 ${meter.badgeColor}`}>
                      {meter.label}
                    </span>
                  </>
                )}
              </div>
              <div className="flex flex-wrap items-center gap-2 text-xs text-content-muted">
                {cr ? (
                  <>
                    <span className="flex items-center gap-1 font-mono text-content-secondary">
                      <GitBranch className="w-3.5 h-3.5 text-content-subtle" />
                      {cr.branchName}
                    </span>
                    <span>·</span>
                    <span className="font-mono">v{cr.baseVersion?.version ?? 0} → v{cr.headVersion.version}</span>
                    <span>·</span>
                    <span>{affectedTableCount} table{affectedTableCount !== 1 ? 's' : ''} modified</span>
                    <span>·</span>
                    <span className="font-mono">{new Date(cr.createdAt).toLocaleDateString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</span>
                  </>
                ) : (
                  <span>Inspect, certify and safely govern database changes before production deployment.</span>
                )}
              </div>
            </div>

            {/* Top-Right Action Controls */}
            <div className="flex flex-wrap items-center gap-2.5 shrink-0">
              {cr && cr.status === 'PendingReview' && mainTab === 'review' && (
                <>
                  <button
                    onClick={() => handleDecide('Rejected')}
                    disabled={isDeciding}
                    className="flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-danger-text/10 text-content-secondary hover:text-danger-text border border-line text-xs font-semibold transition-all disabled:opacity-50 cursor-pointer shadow-xs"
                  >
                    <X className="w-3.5 h-3.5" />
                    <span>Reject</span>
                  </button>
                  <button
                    onClick={() => handleDecide('Approved')}
                    disabled={isDeciding}
                    className="flex items-center gap-1.5 px-4 py-1.5 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold transition-all disabled:opacity-50 cursor-pointer shadow-sm"
                  >
                    {isDeciding ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-4 h-4" />}
                    <span>Approve Changes</span>
                  </button>
                </>
              )}
            </div>
          </div>

          {/* 3. Shopify Polaris Ana Sekmeleri (Main Console Tabs) ───────────────── */}
          <div className="px-4 sm:px-6 border-b border-line bg-surface-850/40 flex items-center justify-between gap-3 overflow-x-auto">
            <div className="flex items-center gap-1">
              {[
                { id: 'review' as MainTab, label: cr ? (cr.title ? `Review: ${cr.title}` : 'Active Review') : 'Active Review', icon: GitPullRequest },
                { id: 'all_requests' as MainTab, label: 'All Requests', icon: ListFilter, count: requestsList ? requestsList.length : undefined },
                { id: 'team' as MainTab, label: 'Team', icon: Users },
                { id: 'keys' as MainTab, label: 'API Keys', icon: KeyRound },
              ].map(t => (
                <button
                  key={t.id}
                  onClick={() => setMainTab(t.id)}
                  className={`flex items-center gap-2 px-3.5 py-3 text-xs font-semibold border-b-2 transition-all whitespace-nowrap cursor-pointer ${
                    mainTab === t.id
                      ? 'border-namines-teal-vibrant text-content-primary font-bold'
                      : 'border-transparent text-content-muted hover:text-content-secondary'
                  }`}
                >
                  <t.icon className={`w-3.5 h-3.5 ${mainTab === t.id ? 'text-namines-teal-vibrant' : 'text-content-muted'}`} />
                  <span>{t.label}</span>
                  {t.count !== undefined && (
                    <span className="text-[10px] font-mono px-1.5 py-0.2 rounded-full bg-surface-700 text-content-muted">
                      {t.count}
                    </span>
                  )}
                </button>
              ))}
            </div>

            {/* Quick Switcher dropdown if viewing a review and multiple exist */}
            {mainTab === 'review' && requestsList && requestsList.length > 1 && (
              <div className="flex items-center gap-2 shrink-0 py-2">
                <span className="text-micro font-bold uppercase tracking-wider text-content-muted hidden md:inline">Change Request:</span>
                <Select
                  value={currentCrId || ''}
                  onChange={e => handleSelectCr(e.target.value)}
                  aria-label="Change Request"
                >
                  {requestsList.map(r => (
                    <option key={r.id} value={r.id}>
                      {r.title || 'Review'} ({r.status})
                    </option>
                  ))}
                </Select>
              </div>
            )}
          </div>

          {/* 4. Konsol Gövdesi (Console Content) ─────────────────────────────── */}
          
          {/* TAB: ACTIVE REVIEW ─────────────────────────────────────────────── */}
          {mainTab === 'review' && (
            <div className="flex flex-col">
              {loadingCr ? (
                <div className="py-24 flex flex-col items-center justify-center gap-3">
                  <Loader2 className="w-7 h-7 text-namines-teal-vibrant animate-spin" />
                  <span className="text-xs font-semibold text-content-muted font-mono tracking-wider">LOADING REVIEW DETAILS…</span>
                </div>
              ) : crError || !cr ? (
                <div className="py-16 px-6 text-center flex flex-col items-center justify-center">
                  <AlertTriangle className="w-9 h-9 text-danger-text mb-3" />
                  <h3 className="text-sm font-bold text-content-primary">No Active Change Request</h3>
                  <p className="text-xs text-content-muted mt-1 max-w-md">
                    {crError || 'Select a change request from the list or submit a new proposal from the diagram.'}
                  </p>
                  <div className="flex items-center gap-3 mt-4">
                    <button
                      onClick={() => setMainTab('all_requests')}
                      className="px-4 py-2 bg-surface-700 hover:bg-surface-600 text-content-primary text-xs font-semibold rounded-[var(--radius-control)] border border-line cursor-pointer"
                    >
                      Browse Change Requests
                    </button>
                    <button
                      onClick={() => router.push('/canvas')}
                      className="px-4 py-2 bg-namines-teal-vibrant hover:brightness-105 text-surface-900 text-xs font-bold rounded-[var(--radius-control)] cursor-pointer"
                    >
                      {returnLabel}
                    </button>
                  </div>
                </div>
              ) : (
                <>
                  {/* Summary Metric Ribbon ─────────────────────────────────── */}
                  <div className="px-5 sm:px-6 py-3 bg-surface-900/50 border-b border-line flex flex-wrap items-center justify-between gap-4 text-xs">
                    <div className="flex flex-wrap items-center gap-5 sm:gap-7">
                      <div className="flex items-center gap-2">
                        <span className="text-content-muted">Approvals:</span>
                        <span className="font-mono font-bold text-content-primary">{cr.approvedCount} / {cr.requiredApprovals} required</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-content-muted">Breaking Changes:</span>
                        <span className={`font-mono font-bold ${breakingCount > 0 ? 'text-danger-text' : 'text-success-text'}`}>
                          {breakingCount}
                        </span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-content-muted">Lock Risk:</span>
                        <span className="font-mono font-bold text-content-primary">{impact?.lockRisks.length ?? 0}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-content-muted">Rollback:</span>
                        <span className={`font-bold flex items-center gap-1 ${impact?.rollback.isReversible ? 'text-success-text' : 'text-danger-text'}`}>
                          {impact?.rollback.isReversible ? <Check className="w-3.5 h-3.5" /> : <X className="w-3.5 h-3.5" />}
                          {impact?.rollback.isReversible ? 'Guaranteed Reversible' : 'Manual Required'}
                        </span>
                      </div>
                    </div>

                    {/* Risk Level Bar */}
                    <div className="flex items-center gap-2.5 min-w-[170px]">
                      <span className="text-[11px] text-content-muted font-bold uppercase tracking-wider">{meter.label}</span>
                      <div className="flex-1 h-2 bg-surface-700 rounded-full overflow-hidden">
                        <div className={`h-full rounded-full ${meter.barColor}`} style={{ width: `${meter.fill}%` }} />
                      </div>
                    </div>
                  </div>

                  {/* Review Detail Sub-Tabs ────────────────────────────────── */}
                  <div className="px-4 sm:px-6 pt-2.5 border-b border-line bg-surface-850/20 flex items-center justify-between gap-2 overflow-x-auto">
                    <div className="flex items-center gap-1.5">
                      {([
                        { id: 'DIFF' as ReviewSubTab, label: `Changes (${affectedTableCount})`, icon: Table2 },
                        { id: 'SQL' as ReviewSubTab, label: 'Migration SQL', icon: Terminal },
                        { id: 'IMPACT' as ReviewSubTab, label: 'Safety & Impact', icon: ShieldCheck, badge: breakingCount > 0 ? `${breakingCount}` : undefined },
                        { id: 'TESTS' as ReviewSubTab, label: 'Container Test', icon: PlayCircle },
                        { id: 'CODE' as ReviewSubTab, label: 'Affected Code', icon: FileSearch },
                        { id: 'HISTORY' as ReviewSubTab, label: `History (${auditLog.length})`, icon: History },
                      ]).map(t => (
                        <button
                          key={t.id}
                          onClick={() => setReviewSubTab(t.id)}
                          className={`flex items-center gap-1.5 px-3 py-2 text-xs font-semibold border-b-2 transition-all whitespace-nowrap cursor-pointer ${
                            reviewSubTab === t.id
                              ? 'border-namines-teal-vibrant text-content-primary'
                              : 'border-transparent text-content-muted hover:text-content-primary hover:border-line'
                          }`}
                        >
                          <t.icon className={`w-3.5 h-3.5 ${reviewSubTab === t.id ? 'text-namines-teal-vibrant' : 'text-content-muted'}`} />
                          <span>{t.label}</span>
                          {t.badge && (
                            <span className="text-[10px] font-bold px-1.5 py-0.2 rounded-full bg-danger-text/20 text-danger-text">
                              {t.badge}
                            </span>
                          )}
                        </button>
                      ))}
                    </div>

                    {reviewSubTab === 'SQL' && cr.migration?.upCode && (
                      <button
                        onClick={copyMigrationSql}
                        className="flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 border border-line text-xs font-semibold text-content-primary transition-colors cursor-pointer shrink-0"
                        title="Copy SQL Script"
                      >
                        {copiedCode ? <Check className="w-3.5 h-3.5 text-success-text" /> : <Copy className="w-3.5 h-3.5" />}
                        <span>{copiedCode ? 'Copied' : 'Copy SQL'}</span>
                      </button>
                    )}
                  </div>

                  {/* Sub-tab Content Area ──────────────────────────────────── */}
                  <div className="p-5 sm:p-6 min-h-[380px] bg-surface-800">

                    {/* SUB-TAB 1: DIFF */}
                    {reviewSubTab === 'DIFF' && (
                      <div className="space-y-4">
                        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                          <div className="relative flex-1 max-w-sm">
                            <input
                              type="text"
                              value={searchTable}
                              onChange={e => setSearchTable(e.target.value)}
                              placeholder="Search tables and columns…"
                              className="w-full bg-surface-900 border border-line rounded-[var(--radius-control)] pl-8 pr-3 py-1.5 text-xs text-content-primary placeholder:text-content-muted focus:outline-none focus:border-namines-teal-vibrant"
                            />
                            <Table2 className="w-3.5 h-3.5 text-content-muted absolute left-2.5 top-2.5" />
                          </div>
                          <span className="text-xs text-content-muted font-mono">
                            Showing {filteredTables.length} of {affectedTableCount} tables
                          </span>
                        </div>

                        {filteredTables.length === 0 ? (
                          <div className="text-center py-12 text-content-muted text-xs border border-dashed border-line rounded-[var(--radius-card)]">
                            No tables match your filter.
                          </div>
                        ) : (
                          <div className="space-y-2.5">
                            {filteredTables.map(t => (
                              <div
                                key={t.tableName}
                                className="p-3.5 rounded-[var(--radius-card)] bg-surface-900 border border-line flex flex-col sm:flex-row sm:items-center justify-between gap-3 transition-colors hover:border-line-strong"
                              >
                                <div className="space-y-1 min-w-0">
                                  <div className="flex items-center gap-2">
                                    <Table2 className="w-4 h-4 text-namines-teal-vibrant shrink-0" />
                                    <span className="text-xs font-bold text-content-primary font-mono">{t.tableName}</span>
                                  </div>
                                  <div className="flex flex-wrap gap-1.5 pt-0.5">
                                    {t.changedColumns.map(col => (
                                      <span
                                        key={col}
                                        className="text-[11px] font-mono px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 text-content-secondary border border-line"
                                      >
                                        {col}
                                      </span>
                                    ))}
                                  </div>
                                </div>
                                <span className={`text-[10px] font-bold uppercase tracking-wider px-2.5 py-0.5 rounded-full shrink-0 self-start sm:self-auto ${
                                  t.kind === 'Added' ? 'bg-success-text/15 text-success-text border border-success-text/25'
                                    : t.kind === 'Removed' ? 'bg-danger-text/15 text-danger-text border border-danger-text/25'
                                    : 'bg-accent-subtle text-accent-text border border-accent/25'
                                }`}>
                                  {t.kind}
                                </span>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    )}

                    {/* SUB-TAB 2: SQL */}
                    {reviewSubTab === 'SQL' && (
                      <div className="space-y-3">
                        <div className="flex items-center justify-between text-xs text-content-muted">
                          <span>Compiled migration DDL statements for the target database engine.</span>
                          <span className="font-mono text-[11px]">C# / SQL DDL</span>
                        </div>
                        {cr.migration?.upCode ? (
                          <div className="rounded-[var(--radius-card)] bg-surface-900 border border-line p-4 overflow-x-auto shadow-inner">
                            <pre className="!bg-transparent !m-0 !p-0 !text-xs font-mono leading-relaxed">
                              <code ref={codeRef} className="language-csharp">{cr.migration.upCode}</code>
                            </pre>
                          </div>
                        ) : (
                          <div className="text-center py-16 text-content-muted text-xs border border-dashed border-line rounded-[var(--radius-card)]">
                            No migration script generated for this revision.
                          </div>
                        )}
                      </div>
                    )}

                    {/* SUB-TAB 3: IMPACT */}
                    {reviewSubTab === 'IMPACT' && (
                      <div className="space-y-4">
                        {cr.aiExplanation && (
                          <div className="p-4 rounded-[var(--radius-card)] bg-accent-subtle/30 border border-accent/20">
                            <div className="flex items-center gap-2 text-xs font-bold text-accent-text uppercase tracking-wider mb-2">
                              <Sparkles className="w-4 h-4" />
                              <span>AI Safety & Impact Summary</span>
                            </div>
                            <p className="text-xs text-content-secondary leading-relaxed whitespace-pre-line font-medium">
                              {cr.aiExplanation}
                            </p>
                          </div>
                        )}

                        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                          <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line space-y-2">
                            <div className="flex items-center gap-2 text-xs font-bold text-content-primary">
                              <AlertTriangle className={`w-4 h-4 ${breakingCount > 0 ? 'text-danger-text' : 'text-content-muted'}`} />
                              <span>Breaking Changes ({breakingCount})</span>
                            </div>
                            {breakingCount === 0 ? (
                              <p className="text-xs text-content-subtle">No breaking changes detected.</p>
                            ) : (
                              impact?.breakingChanges.map((b, i) => (
                                <div key={i} className="p-2.5 rounded-[var(--radius-control)] bg-surface-800 border border-danger-text/25 text-xs">
                                  <p className="text-content-primary font-medium">{b.description}</p>
                                  {b.suggestedMitigation && <p className="text-[11px] text-content-muted mt-1">Mitigation: {b.suggestedMitigation}</p>}
                                </div>
                              ))
                            )}
                          </div>

                          <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line space-y-2">
                            <div className="flex items-center gap-2 text-xs font-bold text-content-primary">
                              <Trash2 className="w-4 h-4 text-content-muted" />
                              <span>Data Loss Risks ({impact?.dataLossRisks.length ?? 0})</span>
                            </div>
                            {(impact?.dataLossRisks.length ?? 0) === 0 ? (
                              <p className="text-xs text-content-subtle">No destructive drops or truncations detected.</p>
                            ) : (
                              impact?.dataLossRisks.map((d, i) => (
                                <div key={i} className="p-2.5 rounded-[var(--radius-control)] bg-surface-800 border border-line text-xs">
                                  <p className="text-content-primary font-medium">{d.tableName}{d.columnName ? `.${d.columnName}` : ''}</p>
                                  <p className="text-[11px] text-content-muted mt-0.5">{d.reason}</p>
                                </div>
                              ))
                            )}
                          </div>

                          <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line space-y-2">
                            <div className="flex items-center gap-2 text-xs font-bold text-content-primary">
                              <Lock className="w-4 h-4 text-content-muted" />
                              <span>Lock Risks ({impact?.lockRisks.length ?? 0})</span>
                            </div>
                            {(impact?.lockRisks.length ?? 0) === 0 ? (
                              <p className="text-xs text-content-subtle">No prolonged exclusive locks expected.</p>
                            ) : (
                              impact?.lockRisks.map((l, i) => (
                                <div key={i} className="p-2.5 rounded-[var(--radius-control)] bg-surface-800 border border-line text-xs">
                                  <p className="text-content-primary font-medium">{l.operation}</p>
                                  {l.saferAlternative && <p className="text-[11px] text-content-muted mt-0.5">{l.saferAlternative}</p>}
                                </div>
                              ))
                            )}
                          </div>

                          <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line space-y-2">
                            <div className="flex items-center gap-2 text-xs font-bold text-content-primary">
                              <Database className="w-4 h-4 text-content-muted" />
                              <span>Missing Indexes ({impact?.indexSuggestions.length ?? 0})</span>
                            </div>
                            {(impact?.indexSuggestions.length ?? 0) === 0 ? (
                              <p className="text-xs text-content-subtle">All foreign keys and constraints are indexed.</p>
                            ) : (
                              impact?.indexSuggestions.map((s, i) => (
                                <div key={i} className="p-2.5 rounded-[var(--radius-control)] bg-surface-800 border border-line text-xs">
                                  <p className="text-content-primary font-medium">{s.tableName}.{s.columnName}</p>
                                  <p className="text-[11px] text-content-muted mt-0.5">{s.reason}</p>
                                </div>
                              ))
                            )}
                          </div>
                        </div>
                      </div>
                    )}

                    {/* SUB-TAB 4: TESTS */}
                    {reviewSubTab === 'TESTS' && (
                      <div className="space-y-4">
                        <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                          <div>
                            <h4 className="text-xs font-bold text-content-primary">Ephemeral Container Verification</h4>
                            <p className="text-xs text-content-muted mt-0.5 leading-relaxed">
                              Spins up a disposable database container to verify migration DDL and constraints.
                            </p>
                          </div>
                          <button
                            onClick={handleRunTests}
                            disabled={isRunningTests}
                            className="flex items-center gap-2 px-4 py-2 bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold rounded-[var(--radius-control)] transition-all disabled:opacity-60 cursor-pointer shrink-0 shadow-sm"
                          >
                            {isRunningTests ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <PlayCircle className="w-3.5 h-3.5" />}
                            <span>{isRunningTests ? 'Testing…' : 'Run Tests'}</span>
                          </button>
                        </div>

                        {cr.testRun && (
                          <div className={`p-4 rounded-[var(--radius-card)] border ${
                            !cr.testRun.testRunSupported ? 'bg-surface-900 border-line'
                              : cr.testRun.testRunSuccess ? 'bg-success-text/10 border-success-text/30' : 'bg-danger-text/10 border-danger-text/30'
                          }`}>
                            <div className="flex items-center justify-between gap-2 mb-2">
                              <span className={`text-xs font-bold ${cr.testRun.testRunSuccess ? 'text-success-text' : 'text-danger-text'}`}>
                                {cr.testRun.testRunSuccess ? 'Verified against engine' : 'Engine rejected migration'}
                              </span>
                              {cr.testRun.testRunDurationMs != null && (
                                <span className="text-xs font-mono text-content-muted">{cr.testRun.testRunDurationMs}ms</span>
                              )}
                            </div>
                            {cr.testRun.testRunMessage && (
                              <pre className="p-3 rounded-[var(--radius-control)] bg-surface-900 border border-line text-xs font-mono text-content-secondary whitespace-pre-wrap">
                                {cr.testRun.testRunMessage}
                              </pre>
                            )}
                          </div>
                        )}
                      </div>
                    )}

                    {/* SUB-TAB 5: CODE */}
                    {reviewSubTab === 'CODE' && (
                      <div className="space-y-4">
                        <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line space-y-3">
                          <div>
                            <h4 className="text-xs font-bold text-content-primary">Static Application Impact Scanner</h4>
                            <p className="text-xs text-content-muted mt-0.5">Scan frontend or backend files for references to altered tables and columns.</p>
                          </div>
                          <input ref={fileInputRef} type="file" multiple className="hidden" onChange={e => handleFilesSelected(e.target.files)} />
                          <div className="flex items-center gap-2">
                            <button
                              onClick={() => fileInputRef.current?.click()}
                              className="flex items-center gap-1.5 px-3 py-1.5 bg-surface-800 hover:bg-surface-700 text-content-primary text-xs font-semibold rounded-[var(--radius-control)] border border-line cursor-pointer"
                            >
                              <Upload className="w-3.5 h-3.5" />
                              <span>Upload Files</span>
                            </button>
                            <button
                              onClick={handleScanCode}
                              disabled={isScanning || codeFiles.length === 0}
                              className="flex items-center gap-1.5 px-3.5 py-1.5 bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold rounded-[var(--radius-control)] disabled:opacity-50 cursor-pointer shadow-sm"
                            >
                              {isScanning ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <FileSearch className="w-3.5 h-3.5" />}
                              <span>Scan Code {codeFiles.length > 0 ? `(${codeFiles.length})` : ''}</span>
                            </button>
                          </div>
                        </div>

                        {scanResult && (
                          <div className="p-4 rounded-[var(--radius-card)] bg-surface-900 border border-line space-y-2">
                            <span className="text-xs font-bold text-content-primary">
                              Scan Results ({scanResult.matches.length} matches across {scanResult.filesScanned} files)
                            </span>
                            {scanResult.matches.length === 0 ? (
                              <p className="text-xs text-success-text">No references found.</p>
                            ) : (
                              scanResult.matches.map((m, i) => (
                                <div key={i} className="p-2.5 bg-surface-800 border border-line rounded-[var(--radius-control)] space-y-1">
                                  <div className="flex items-center gap-2">
                                    <span className="text-xs font-mono font-bold text-namines-teal-vibrant">{m.fileName}:{m.lineNumber}</span>
                                    <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-surface-700 text-content-secondary border border-line">{m.matchedIdentifier}</span>
                                  </div>
                                  <code className="text-xs text-content-secondary font-mono block whitespace-pre-wrap">{m.lineText}</code>
                                </div>
                              ))
                            )}
                          </div>
                        )}
                      </div>
                    )}

                    {/* SUB-TAB 6: HISTORY */}
                    {reviewSubTab === 'HISTORY' && (
                      <div className="space-y-3">
                        <h4 className="text-xs font-bold text-content-muted uppercase tracking-wider">
                          Change Request Audit Trail ({auditLog.length})
                        </h4>
                        {auditLog.length === 0 ? (
                          <div className="text-center py-10 text-content-muted text-xs border border-dashed border-line rounded-[var(--radius-card)]">
                            No historical actions recorded yet for this review.
                          </div>
                        ) : (
                          <div className="space-y-2">
                            {auditLog.map(entry => (
                              <div key={entry.id} className="p-3 rounded-[var(--radius-card)] bg-surface-900 border border-line flex items-center justify-between gap-4 text-xs">
                                <div>
                                  <p className="font-semibold text-content-primary">
                                    {AUDIT_ACTION_LABEL[entry.action] || entry.action}
                                    {entry.actorUsername ? ` by ${entry.actorUsername}` : entry.actorUserId ? '' : ' (Automatic)'}
                                  </p>
                                  {entry.details && <p className="text-[11px] text-content-muted mt-0.5">{entry.details}</p>}
                                </div>
                                <span className="font-mono text-xs text-content-muted shrink-0">
                                  {new Date(entry.createdAt).toLocaleString()}
                                </span>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    )}

                  </div>

                  {/* Panel Decision Footer ─────────────────────────────────── */}
                  <div className="p-4 sm:p-5 border-t border-line bg-surface-850/80 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                    <div className="flex items-center gap-3 text-xs text-content-secondary">
                      <span className="font-semibold">Review Progress:</span>
                      <span className="font-mono">{cr.approvedCount} of {cr.requiredApprovals} approvals required</span>
                    </div>

                    <div className="flex items-center gap-2.5">
                      {cr.status === 'PendingReview' ? (
                        <>
                          <button
                            onClick={() => handleDecide('Rejected')}
                            disabled={isDeciding}
                            className="flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-900 hover:bg-danger-text/10 text-content-secondary hover:text-danger-text border border-line text-xs font-semibold transition-all disabled:opacity-50 cursor-pointer"
                          >
                            <X className="w-3.5 h-3.5" />
                            <span>Reject</span>
                          </button>
                          <button
                            onClick={() => handleDecide('Approved')}
                            disabled={isDeciding}
                            className="flex items-center gap-1.5 px-4 py-1.5 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold transition-all disabled:opacity-50 cursor-pointer shadow-sm"
                          >
                            {isDeciding ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-4 h-4" />}
                            <span>Approve Changes</span>
                          </button>
                        </>
                      ) : (
                        <span className="text-xs text-content-muted font-medium">
                          Status: <strong className="text-content-primary">{cr.status}</strong>
                        </span>
                      )}

                      <button
                        onClick={() => router.push('/canvas')}
                        className="flex items-center gap-1 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line text-xs font-semibold text-content-muted hover:text-content-primary transition-colors cursor-pointer"
                        title={returnLabel}
                      >
                        <ArrowLeft className="w-3 h-3 text-namines-teal-vibrant" />
                        <span>{returnLabel}</span>
                      </button>
                    </div>
                  </div>
                </>
              )}
            </div>
          )}

          {/* TAB: ALL REQUESTS LIST ─────────────────────────────────────────── */}
          {mainTab === 'all_requests' && (
            <div className="p-5 sm:p-6 space-y-4">
              
              {/* Filter & Settings Strip */}
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 bg-surface-900/60 border border-line rounded-[var(--radius-card)]">
                <div role="group" aria-label="Filter by status" className="flex flex-wrap items-center gap-1.5">
                  {(['all', 'PendingReview', 'Approved', 'Rejected'] as StatusFilter[]).map(f => (
                    <button
                      key={f}
                      type="button"
                      aria-pressed={statusFilter === f}
                      onClick={() => setStatusFilter(f)}
                      className={`h-7 px-3 rounded-full text-xs font-semibold transition-all cursor-pointer ${
                        statusFilter === f
                          ? 'bg-content-primary text-surface-900 shadow-xs'
                          : 'bg-surface-800 text-content-muted hover:text-content-primary border border-line'
                      }`}
                    >
                      {f === 'all' ? 'All' : STATUS_CONFIG[f].label}
                      <span className="ml-1.5 font-mono opacity-70 text-[10px]">{requestCounts[f]}</span>
                    </button>
                  ))}
                </div>

                {autoApproveSafe !== null && (
                  <div className="flex items-center gap-2 pt-2 sm:pt-0 border-t sm:border-t-0 border-line text-xs text-content-muted shrink-0">
                    <span>Auto-approve safe:</span>
                    <Switch
                      checked={autoApproveSafe}
                      disabled={isTogglingAutoApprove}
                      onChange={handleToggleAutoApprove}
                      label="Auto-approve safe changes"
                    />
                  </div>
                )}
              </div>

              {/* List Cards */}
              {requestsList === null ? (
                <div className="space-y-3 py-2">
                  <Skeleton className="h-16 w-full rounded-[var(--radius-card)]" />
                  <Skeleton className="h-16 w-full rounded-[var(--radius-card)]" />
                  <Skeleton className="h-16 w-full rounded-[var(--radius-card)]" />
                </div>
              ) : requestsList.length === 0 ? (
                <div className="py-14 text-center border border-dashed border-line rounded-[var(--radius-card)] p-6 space-y-3">
                  <Boxes className="w-8 h-8 text-content-muted mx-auto" />
                  <div>
                    <h3 className="text-sm font-bold text-content-primary">No change requests yet</h3>
                    <p className="text-xs text-content-muted mt-1 max-w-sm mx-auto">
                      Open the diagram and press “Request Review” to propose your schema for verification.
                    </p>
                  </div>
                  <button
                    onClick={() => router.push('/canvas')}
                    className="inline-flex items-center gap-2 px-4 py-2 bg-namines-teal-vibrant hover:brightness-105 text-surface-900 text-xs font-bold rounded-[var(--radius-control)] cursor-pointer shadow-xs"
                  >
                    <ArrowLeft className="w-3.5 h-3.5" />
                    <span>{returnLabel}</span>
                  </button>
                </div>
              ) : filteredRequests.length === 0 ? (
                <div className="py-10 text-center text-xs text-content-muted border border-dashed border-line rounded-[var(--radius-card)]">
                  No change requests match this status filter.
                </div>
              ) : (
                <div className="space-y-2.5">
                  {filteredRequests.map(r => {
                    const risk = RISK_CONFIG[r.riskLevel];
                    const status = STATUS_CONFIG[r.status] ?? { label: r.status, tone: 'neutral' as const, icon: Clock };
                    const isSelected = r.id === currentCrId;

                    return (
                      <button
                        key={r.id}
                        type="button"
                        onClick={() => handleSelectCr(r.id)}
                        className={`group w-full text-left rounded-[var(--radius-card)] border p-4 transition-all cursor-pointer ${
                          isSelected
                            ? 'border-namines-teal-vibrant/60 bg-surface-700/60 shadow-xs'
                            : 'border-line bg-surface-900/60 hover:border-line-strong hover:bg-surface-700/40'
                        }`}
                      >
                        <div className="flex items-start justify-between gap-4">
                          <div className="min-w-0">
                            <div className="flex items-center gap-2">
                              <p className="text-xs font-bold text-content-primary truncate">{r.title || 'Schema Update'}</p>
                              {isSelected && (
                                <span className="text-[10px] font-bold px-1.5 py-0.2 rounded-full bg-namines-teal-vibrant/20 text-namines-teal-vibrant">
                                  Viewing
                                </span>
                              )}
                            </div>
                            <p className="mt-1 text-[11px] text-content-muted font-mono">
                              {r.branchName} · {r.tableCount} tables · {new Date(r.createdAt).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })}
                            </p>
                          </div>
                          <ArrowRight className="w-4 h-4 text-content-subtle group-hover:text-content-primary group-hover:translate-x-0.5 transition-all shrink-0 mt-0.5" />
                        </div>

                        <div className="mt-3 flex flex-wrap items-center gap-2">
                          <span className={`text-[10px] font-bold px-2 py-0.5 rounded-full uppercase tracking-wider ${risk.badgeColor}`}>
                            {risk.label}
                          </span>
                          <span className={`text-[10px] font-bold px-2 py-0.5 rounded-full uppercase tracking-wider ${
                            r.status === 'Approved' ? 'bg-success-text/15 text-success-text border border-success-text/25'
                              : r.status === 'Rejected' ? 'bg-danger-text/15 text-danger-text border border-danger-text/25'
                              : 'bg-accent-subtle text-accent-text border border-accent/25'
                          }`}>
                            {status.label}
                          </span>
                          {r.status === 'PendingReview' && (
                            <span className="text-[11px] text-content-muted font-mono">
                              {r.approvedCount}/{r.requiredApprovals} approvals
                            </span>
                          )}
                        </div>
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* TAB: TEAM ──────────────────────────────────────────────────────── */}
          {mainTab === 'team' && (
            <div className="p-5 sm:p-6">
              <TeamPanel projectId={activeProjectId || 'local-project'} currentUserEmail={currentUserEmail} />
            </div>
          )}

          {/* TAB: API KEYS ──────────────────────────────────────────────────── */}
          {mainTab === 'keys' && (
            <div className="p-5 sm:p-6">
              <GatewayKeyPanel projectId={activeProjectId || 'local-project'} />
            </div>
          )}

        </div>

      </div>

    </div>
  );
}
