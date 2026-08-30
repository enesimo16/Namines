'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import {
  ArrowLeft, Loader2, AlertTriangle, Trash2, Lock, Database, GitBranch,
  Check, X, Table2, ArrowRightLeft, PlayCircle, FileSearch, Upload, Sparkles
} from 'lucide-react';
import Prism from 'prismjs';
import 'prismjs/components/prism-csharp';
import 'prismjs/themes/prism-tomorrow.css';
import { changeRequestService } from '../../../services/api';
import { useToastStore } from '../../../store/useToastStore';
import { ChangeRequestDetail, RiskLevel, AffectedCodeScanResult, ChangeRequestAuditEntry } from '../../../types/changeRequest';

type Tab = 'DIFF' | 'SQL' | 'IMPACT' | 'TESTS' | 'CODE';

const RISK_METER: Record<RiskLevel, { label: string; fill: number; color: string }> = {
  Safe: { label: 'SAFE', fill: 25, color: 'bg-success-text' },
  Risky: { label: 'MEDIUM', fill: 55, color: 'bg-content-secondary' },
  Destructive: { label: 'HIGH', fill: 80, color: 'bg-danger-text' },
  Breaking: { label: 'CRITICAL', fill: 100, color: 'bg-danger-text' },
};

const CHANGE_KIND_COLOR: Record<string, string> = {
  Added: 'text-success-text bg-success-text/10',
  Removed: 'text-danger-text bg-danger-text/10',
  Modified: 'text-content-secondary bg-white/[0.08]',
  RenamedFrom: 'text-accent-text bg-white/[0.08]',
};

export default function ChangeRequestDetailPage() {
  const params = useParams();
  const router = useRouter();
  const showToast = useToastStore(state => state.showToast);
  const id = params?.id as string;

  const [cr, setCr] = useState<ChangeRequestDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<Tab>('DIFF');
  const [isDeciding, setIsDeciding] = useState(false);
  const [isRunningTests, setIsRunningTests] = useState(false);
  const [codeFiles, setCodeFiles] = useState<{ fileName: string; content: string }[]>([]);
  const [scanResult, setScanResult] = useState<AffectedCodeScanResult | null>(null);
  const [isScanning, setIsScanning] = useState(false);
  const [auditLog, setAuditLog] = useState<ChangeRequestAuditEntry[]>([]);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const codeRef = useRef<HTMLElement>(null);

  const load = () => {
    if (!id) return;
    changeRequestService.getDetail(id)
      .then(setCr)
      .catch(() => setError('Failed to load this change request.'));
    changeRequestService.getAuditLog(id)
      .then(setAuditLog)
      .catch(() => {});
  };

  useEffect(load, [id]);

  useEffect(() => {
    if (activeTab === 'SQL' && codeRef.current && cr?.migration?.upCode) {
      Prism.highlightElement(codeRef.current);
    }
  }, [activeTab, cr]);

  const handleRunTests = async () => {
    if (!cr) return;
    setIsRunningTests(true);
    try {
      const result = await changeRequestService.runTests(cr.id);
      showToast(
        result.supported
          ? (result.success ? 'Migration applied successfully in the test container.' : 'The engine rejected the migration — see details below.')
          : 'Live test execution isn\'t available for this engine yet.',
        result.supported && result.success ? 'success' : 'warning'
      );
      load();
    } catch (err: any) {
      showToast(err?.response?.data?.error || 'Failed to run the test. Is Docker running?', 'error');
    } finally {
      setIsRunningTests(false);
    }
  };

  const handleFilesSelected = async (fileList: FileList | null) => {
    if (!fileList || fileList.length === 0) return;
    const read = await Promise.all(Array.from(fileList).map(f => f.text().then(content => ({ fileName: f.name, content }))));
    setCodeFiles(prev => [...prev, ...read]);
  };

  const handleScanCode = async () => {
    if (!cr || codeFiles.length === 0) return;
    setIsScanning(true);
    try {
      const result = await changeRequestService.scanAffectedCode(cr.id, codeFiles);
      setScanResult(result);
      showToast(
        result.matches.length > 0
          ? `Found ${result.matches.length} possible match${result.matches.length > 1 ? 'es' : ''} across ${result.filesScanned} file(s).`
          : 'No possible matches found in the uploaded files.',
        result.matches.length > 0 ? 'warning' : 'success'
      );
    } catch {
      showToast('Failed to scan the uploaded files.', 'error');
    } finally {
      setIsScanning(false);
    }
  };

  const handleDecide = async (decision: 'Approved' | 'Rejected') => {
    if (!cr) return;
    setIsDeciding(true);
    try {
      await changeRequestService.decide(cr.id, decision);
      showToast(decision === 'Approved' ? 'Change request approved.' : 'Change request rejected.', decision === 'Approved' ? 'success' : 'info');
      load();
    } catch (err: any) {
      const msg = err?.response?.data?.error
        || (err?.response?.status === 403 ? 'You cannot approve your own high-risk change — a different reviewer is required.' : null)
        || 'Failed to record your decision.';
      showToast(msg, 'error');
    } finally {
      setIsDeciding(false);
    }
  };

  if (error) {
    return (
      <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex items-center justify-center">
        <p className="text-sm text-danger-text">{error}</p>
      </div>
    );
  }

  if (!cr) {
    return (
      <div className="min-h-[calc(100vh-56px)] bg-surface-900 flex items-center justify-center">
        <Loader2 className="w-6 h-6 text-content-muted animate-spin" />
      </div>
    );
  }

  const impact = cr.impact;
  const meter = RISK_METER[cr.riskLevel];
  const breakingCount = impact.breakingChanges.length;
  const affectedTableCount = impact.affectedTables.length;

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary font-sans">
      <div className="max-w-4xl mx-auto px-6 py-8">
        <button
          onClick={() => router.push('/review')}
          className="flex items-center gap-2 text-xs font-semibold text-content-muted hover:text-content-primary transition-colors cursor-pointer mb-6"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>All Change Requests</span>
        </button>

        {/* Header */}
        <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-5 mb-4">
          <div className="flex items-center gap-2 text-[11px] text-content-subtle mb-2">
            <GitBranch className="w-3.5 h-3.5" />
            <span>{cr.branchName}</span>
            <span className="text-content-subtle">·</span>
            <span>v{cr.baseVersion?.version ?? 0} → v{cr.headVersion.version}</span>
          </div>
          <h1 className="text-base font-bold text-content-primary mb-3">{cr.title || 'Schema update'}</h1>

          <div className="flex flex-wrap items-center gap-3 text-xs text-content-secondary mb-4">
            <span>{affectedTableCount} tables affected</span>
            <span className="text-content-subtle">·</span>
            <span>{impact.affectedIndexes.length} index changes</span>
            {breakingCount > 0 && (
              <>
                <span className="text-content-subtle">·</span>
                <span className="text-danger-text">{breakingCount} potential breaking change{breakingCount > 1 ? 's' : ''}</span>
              </>
            )}
          </div>

          <div className="space-y-1.5">
            <div className="flex items-center justify-between text-[10px] font-bold uppercase tracking-wider">
              <span className="text-content-subtle">Risk</span>
              <span className="text-content-secondary">{meter.label}</span>
            </div>
            <div className="w-full h-2 bg-surface-600 rounded-full overflow-hidden">
              <div className={`h-full rounded-full ${meter.color}`} style={{ width: `${meter.fill}%` }} />
            </div>
          </div>
        </div>

        {/* Tabs */}
        <div className="flex gap-1 mb-4 bg-surface-700 border border-content-primary/15 rounded-xl p-1">
          {([
            { id: 'DIFF', label: 'Schema Diff' },
            { id: 'SQL', label: 'Migration Code' },
            { id: 'IMPACT', label: 'Impact Analysis' },
            { id: 'TESTS', label: 'Run Tests' },
            { id: 'CODE', label: 'Affected Code' },
          ] as { id: Tab; label: string }[]).map(t => (
            <button
              key={t.id}
              onClick={() => setActiveTab(t.id)}
              className={`flex-1 text-xs font-semibold py-2 rounded-lg transition-all cursor-pointer ${
                activeTab === t.id ? 'bg-white/[0.1] text-content-primary' : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        {/* Tab content */}
        <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-5 mb-4 min-h-[240px]">
          {activeTab === 'DIFF' && (
            <div className="space-y-4">
              {impact.affectedTables.length === 0 ? (
                <p className="text-xs text-content-subtle">No table-level changes.</p>
              ) : (
                <div className="space-y-2">
                  {impact.affectedTables.map((t, i) => (
                    <div key={i} className="flex items-center justify-between p-3 bg-surface-600 rounded-lg">
                      <div className="flex items-center gap-2.5">
                        <Table2 className="w-4 h-4 text-content-muted" />
                        <div>
                          <p className="text-xs font-semibold text-content-primary">
                            {t.kind === 'RenamedFrom' ? `${t.previousName} → ${t.tableName}` : t.tableName}
                          </p>
                          {t.changedColumns.length > 0 && (
                            <p className="text-[10px] text-content-subtle mt-0.5">{t.changedColumns.join(', ')}</p>
                          )}
                        </div>
                      </div>
                      <span className={`text-micro font-bold uppercase tracking-wider px-2 py-0.5 rounded-full ${CHANGE_KIND_COLOR[t.kind]}`}>
                        {t.kind === 'RenamedFrom' ? 'Renamed' : t.kind}
                      </span>
                    </div>
                  ))}
                </div>
              )}

              {impact.affectedRelations.length > 0 && (
                <div className="space-y-2 pt-2 border-t border-content-primary/10">
                  <p className="text-[10px] font-bold text-content-subtle uppercase tracking-wider">Relations</p>
                  {impact.affectedRelations.map((r, i) => (
                    <div key={i} className="flex items-center gap-2.5 text-xs text-content-secondary">
                      <ArrowRightLeft className="w-3.5 h-3.5 text-content-muted" />
                      <span>{r.fromTable} → {r.toTable}</span>
                      <span className={`text-micro font-bold uppercase tracking-wider px-1.5 py-0.5 rounded-full ${CHANGE_KIND_COLOR[r.kind]}`}>
                        {r.kind}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {activeTab === 'SQL' && (
            cr.migration?.upCode ? (
              <pre className="!bg-transparent !m-0 !p-0 !text-xs overflow-x-auto">
                <code ref={codeRef} className="language-csharp">{cr.migration.upCode}</code>
              </pre>
            ) : (
              <p className="text-xs text-content-subtle">
                Migration code isn't available for this change (AI code generation may be unavailable). Schema Diff and Impact Analysis are still accurate.
              </p>
            )
          )}

          {activeTab === 'IMPACT' && (
            <div className="space-y-5">
              {cr.aiExplanation && (
                <div className="p-3.5 bg-accent-subtle rounded-lg">
                  <div className="flex items-center gap-1.5 text-[10px] font-bold text-accent-text uppercase tracking-wider mb-1.5">
                    <Sparkles className="w-3.5 h-3.5" />
                    <span>AI Summary</span>
                  </div>
                  <p className="text-xs text-content-secondary leading-relaxed whitespace-pre-line">{cr.aiExplanation}</p>
                  <p className="text-[10px] text-content-subtle mt-2">Generated from the structural analysis below — not an independent finding.</p>
                </div>
              )}
              <ImpactSection
                icon={<AlertTriangle className="w-3.5 h-3.5" />}
                title="Breaking Changes"
                empty="No breaking changes detected."
                items={impact.breakingChanges.map((b, i) => (
                  <div key={i} className="p-3 bg-surface-600 rounded-lg">
                    <p className="text-xs text-content-primary">{b.description}</p>
                    {b.suggestedMitigation && (
                      <p className="text-[10px] text-content-subtle mt-1">Suggestion: {b.suggestedMitigation}</p>
                    )}
                  </div>
                ))}
              />
              <ImpactSection
                icon={<Trash2 className="w-3.5 h-3.5" />}
                title="Data Loss Risks"
                empty="No data loss risks detected."
                items={impact.dataLossRisks.map((d, i) => (
                  <div key={i} className="p-3 bg-surface-600 rounded-lg">
                    <p className="text-xs text-content-primary">{d.tableName}{d.columnName ? `.${d.columnName}` : ''}</p>
                    <p className="text-[10px] text-content-subtle mt-1">{d.reason}</p>
                  </div>
                ))}
              />
              <ImpactSection
                icon={<Lock className="w-3.5 h-3.5" />}
                title="Lock Risks"
                empty="No blocking locks expected."
                items={impact.lockRisks.map((l, i) => (
                  <div key={i} className="p-3 bg-surface-600 rounded-lg flex items-center justify-between gap-3">
                    <div>
                      <p className="text-xs text-content-primary">{l.operation}{l.tableName ? ` — ${l.tableName}` : ''}</p>
                      {l.saferAlternative && <p className="text-[10px] text-content-subtle mt-1">{l.saferAlternative}</p>}
                    </div>
                    <span className={`text-micro font-bold uppercase tracking-wider px-2 py-0.5 rounded-full shrink-0 ${
                      l.severity === 'Blocking' ? 'text-danger-text bg-danger-text/10' : 'text-content-subtle bg-white/[0.04]'
                    }`}>
                      {l.severity}
                    </span>
                  </div>
                ))}
              />
              <ImpactSection
                icon={<Database className="w-3.5 h-3.5" />}
                title="Index Suggestions"
                empty="No missing indexes detected."
                items={impact.indexSuggestions.map((s, i) => (
                  <div key={i} className="p-3 bg-surface-600 rounded-lg">
                    <p className="text-xs text-content-primary">{s.tableName}.{s.columnName}</p>
                    <p className="text-[10px] text-content-subtle mt-1">{s.reason}</p>
                  </div>
                ))}
              />

              <div className="pt-2 border-t border-content-primary/10">
                <p className="text-[10px] font-bold text-content-subtle uppercase tracking-wider mb-1.5">Rollback</p>
                <p className={`text-xs ${impact.rollback.isReversible ? 'text-success-text' : 'text-danger-text'}`}>
                  {impact.rollback.isReversible ? 'This change can be safely rolled back.' : (impact.rollback.reason || 'This change cannot be automatically rolled back.')}
                </p>
              </div>
            </div>
          )}

          {activeTab === 'TESTS' && (
            <div className="space-y-4">
              <p className="text-xs text-content-muted leading-relaxed">
                Spins up a real, disposable database container, applies this migration to it, and reports
                whether the engine actually accepted it — proof, not a prediction.
              </p>

              <button
                onClick={handleRunTests}
                disabled={isRunningTests}
                className="flex items-center gap-2 px-4 py-2.5 bg-content-primary hover:bg-content-primary-hover text-surface-900 text-xs font-semibold rounded-lg transition-all disabled:opacity-60 cursor-pointer"
              >
                {isRunningTests ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <PlayCircle className="w-3.5 h-3.5" />}
                {isRunningTests ? 'Running in a test container…' : 'Run Tests'}
              </button>

              {cr.testRun && (
                <div className={`p-3.5 rounded-lg ${
                  !cr.testRun.testRunSupported ? 'bg-white/[0.04]'
                    : cr.testRun.testRunSuccess ? 'bg-success-text/10' : 'bg-danger-text/10'
                }`}>
                  <div className="flex items-center gap-2">
                    {!cr.testRun.testRunSupported ? (
                      <span className="text-xs font-semibold text-content-muted">Not supported for this engine</span>
                    ) : cr.testRun.testRunSuccess ? (
                      <>
                        <Check className="w-4 h-4 text-success-text" />
                        <span className="text-xs font-semibold text-success-text">Migration applied successfully</span>
                      </>
                    ) : (
                      <>
                        <X className="w-4 h-4 text-danger-text" />
                        <span className="text-xs font-semibold text-danger-text">The engine rejected this migration</span>
                      </>
                    )}
                    {cr.testRun.testRunDurationMs != null && (
                      <span className="text-[10px] text-content-subtle ml-auto">{(cr.testRun.testRunDurationMs / 1000).toFixed(1)}s</span>
                    )}
                  </div>
                  {cr.testRun.testRunMessage && (
                    <pre className="mt-2 text-[11px] text-content-secondary whitespace-pre-wrap font-mono leading-relaxed">{cr.testRun.testRunMessage}</pre>
                  )}
                  {cr.testRun.testRunAt && (
                    <p className="text-[10px] text-content-subtle mt-2">Last run {new Date(cr.testRun.testRunAt).toLocaleString()}</p>
                  )}
                </div>
              )}
            </div>
          )}

          {activeTab === 'CODE' && (
            <div className="space-y-4">
              <p className="text-xs text-content-muted leading-relaxed">
                Upload files from your application (models, API routes, queries) and we'll check whether
                the changed table/column names still appear in them. This is a <span className="text-content-secondary font-semibold">possible impact</span>,
                not a certain one — a name match isn't proof of a real dependency.
              </p>

              <input
                ref={fileInputRef}
                type="file"
                multiple
                className="hidden"
                onChange={e => handleFilesSelected(e.target.files)}
              />

              <div className="flex items-center gap-2.5">
                <button
                  onClick={() => fileInputRef.current?.click()}
                  className="flex items-center gap-1.5 px-3.5 py-2 bg-white/[0.06] hover:bg-white/[0.1] text-content-secondary text-xs font-semibold rounded-lg transition-all cursor-pointer"
                >
                  <Upload className="w-3.5 h-3.5" />
                  Add Files
                </button>
                <button
                  onClick={handleScanCode}
                  disabled={isScanning || codeFiles.length === 0}
                  className="flex items-center gap-1.5 px-3.5 py-2 bg-content-primary hover:bg-content-primary-hover text-surface-900 text-xs font-semibold rounded-lg transition-all disabled:opacity-50 cursor-pointer"
                >
                  {isScanning ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <FileSearch className="w-3.5 h-3.5" />}
                  Scan {codeFiles.length > 0 ? `${codeFiles.length} file${codeFiles.length > 1 ? 's' : ''}` : ''}
                </button>
              </div>

              {codeFiles.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {codeFiles.map((f, i) => (
                    <span key={i} className="text-[10px] text-content-muted bg-white/[0.05] px-2 py-1 rounded-md flex items-center gap-1.5">
                      {f.fileName}
                      <button onClick={() => setCodeFiles(prev => prev.filter((_, idx) => idx !== i))} className="hover:text-danger-text cursor-pointer">
                        <X className="w-3 h-3" />
                      </button>
                    </span>
                  ))}
                </div>
              )}

              {scanResult && (
                scanResult.matches.length === 0 ? (
                  <p className="text-xs text-success-text">No possible matches found in the {scanResult.filesScanned} scanned file(s).</p>
                ) : (
                  <div className="space-y-2">
                    {scanResult.matches.map((m, i) => (
                      <div key={i} className="p-3 bg-surface-600 rounded-lg">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="text-[11px] font-semibold text-content-secondary">{m.fileName}:{m.lineNumber}</span>
                          <span className="text-micro font-bold uppercase tracking-wider px-1.5 py-0.5 rounded-full text-content-secondary bg-white/[0.08]">{m.matchedIdentifier}</span>
                        </div>
                        <code className="text-[11px] text-content-muted font-mono">{m.lineText}</code>
                      </div>
                    ))}
                  </div>
                )
              )}
            </div>
          )}
        </div>

        {/* Approvals + decision */}
        <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-5">
          <div className="flex items-center justify-between mb-3">
            <p className="text-xs font-semibold text-content-secondary">
              {cr.approvedCount}/{cr.requiredApprovals} approvals
              {cr.rejectedCount > 0 && <span className="text-danger-text"> · rejected</span>}
            </p>
            <span className={`text-micro font-bold uppercase tracking-wider px-2 py-1 rounded-full ${
              cr.status === 'Approved' ? 'text-success-text bg-success-text/10'
                : cr.status === 'Rejected' ? 'text-danger-text bg-danger-text/10'
                : 'text-content-secondary bg-white/[0.08]'
            }`}>
              {cr.status === 'PendingReview' ? 'Pending Review' : cr.status}
            </span>
          </div>

          {cr.approvals.length > 0 && (
            <div className="space-y-1.5 mb-4">
              {cr.approvals.map(a => (
                <div key={a.id} className="flex items-center gap-2 text-[11px] text-content-muted">
                  {a.decision === 'Approved' ? <Check className="w-3.5 h-3.5 text-success-text" /> : <X className="w-3.5 h-3.5 text-danger-text" />}
                  <span>{a.username || 'A reviewer'} {a.decision === 'Approved' ? 'approved' : 'rejected'} this change</span>
                </div>
              ))}
            </div>
          )}

          {cr.status === 'PendingReview' && (
            <div className="flex items-center justify-end gap-2.5">
              <button
                onClick={() => handleDecide('Rejected')}
                disabled={isDeciding}
                className="flex items-center gap-1.5 px-4 py-2 bg-white/[0.06] hover:bg-danger-text/10 text-content-muted hover:text-danger-text text-xs font-semibold rounded-lg transition-all disabled:opacity-50 cursor-pointer"
              >
                <X className="w-3.5 h-3.5" />
                Reject
              </button>
              <button
                onClick={() => handleDecide('Approved')}
                disabled={isDeciding}
                className="flex items-center gap-1.5 px-4 py-2 bg-content-primary hover:bg-content-primary-hover text-surface-900 text-xs font-semibold rounded-lg transition-all disabled:opacity-50 cursor-pointer"
              >
                {isDeciding ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
                Approve
              </button>
            </div>
          )}
        </div>

        {auditLog.length > 0 && (
          <div className="bg-surface-700 border border-content-primary/15 rounded-xl p-5 mt-4">
            <p className="text-[10px] font-bold text-content-subtle uppercase tracking-wider mb-2.5">History</p>
            <div className="space-y-1.5">
              {auditLog.map(entry => (
                <div key={entry.id} className="flex items-center justify-between gap-3 text-[11px]">
                  <span className="text-content-muted">
                    {AUDIT_ACTION_LABEL[entry.action]}
                    {entry.actorUsername ? ` by ${entry.actorUsername}` : entry.actorUserId ? '' : ' (automatic)'}
                    {entry.details ? ` — ${entry.details}` : ''}
                  </span>
                  <span className="text-content-subtle shrink-0 font-mono">{new Date(entry.createdAt).toLocaleString()}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

const AUDIT_ACTION_LABEL: Record<string, string> = {
  Created: 'Change request created',
  AutoApproved: 'Auto-approved',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

function ImpactSection({ icon, title, empty, items }: { icon: React.ReactNode; title: string; empty: string; items: React.ReactNode[] }) {
  return (
    <div>
      <div className="flex items-center gap-1.5 text-[10px] font-bold text-content-subtle uppercase tracking-wider mb-1.5">
        {icon}
        <span>{title}</span>
      </div>
      {items.length === 0 ? (
        <p className="text-xs text-content-subtle">{empty}</p>
      ) : (
        <div className="space-y-1.5">{items}</div>
      )}
    </div>
  );
}
