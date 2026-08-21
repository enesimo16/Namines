import React, { useState } from 'react';
import {
  X, AlertTriangle, Cpu, Sparkles, Loader2,
  AlertOctagon, Info, Shield,
} from 'lucide-react';
import { DbaIssue, useAIDba } from '../../hooks/useAIDba';
import { useAIGateway } from '../../hooks/useAIGateway';
import { useDbaStore } from '../../store/useDbaStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { schemaService, authService } from '../../services/api';
import { API_ORIGIN } from '../../lib/apiConfig';
import ContextualHelpTooltip from '../help/ContextualHelpTooltip';
import { helpContent } from '../../lib/helpContent';

interface DbaIssuePanelProps {
  isOpen: boolean;
  onClose: () => void;
  issues: DbaIssue[];
  score: number;
  assessment: string;
}

/**
 * DBA paneli artık kenara yaslı, tam yükseklikte kayan bir çekmece —
 * TableEditorDrawer ile aynı dilde (önceki hâli, kenarlardan boşluklu, yuvarlak
 * köşeli "yüzen kart" idi). Renk paletinden sarı/hardal (#a6813f/#c9b27f) ailesi
 * tamamen kaldırıldı — kullanıcı talimatı. Artık yalnızca iki semantik renk var:
 * kırmızı (hata/danger) ve yeşil (FinOps/olumlu); Warning/Info/Security nötr
 * off-white ile, ikon ve etiketle ayrışıyor.
 */
export default function DbaIssuePanel({ isOpen, onClose, issues, score, assessment }: DbaIssuePanelProps) {
  const [filter, setFilter] = useState<'ALL' | 'ERROR' | 'WARNING' | 'INFO'>('ALL');
  const [categoryFilter, setCategoryFilter] = useState<'ALL' | 'Performance' | 'Security' | 'FinOps'>('ALL');
  const selectedTableFilter = useDbaStore(state => state.selectedTableFilter);
  const setSelectedTableFilter = useDbaStore(state => state.setSelectedTableFilter);

  const { schema, loadFromSchema, aiProvider, modelName } = useSchemaStore();
  const { activeProjectId } = useProjectHistoryStore();
  const [isFixing, setIsFixing] = useState(false);
  const [isAnalyzingLocal, setIsAnalyzingLocal] = useState(false);
  const [isCopyingBadge, setIsCopyingBadge] = useState(false);
  const showToast = useToastStore(state => state.showToast);

  const handleCopyBadge = async () => {
    if (!activeProjectId) {
      showToast('Save the project to the cloud first.', 'warning');
      return;
    }
    setIsCopyingBadge(true);
    try {
      // Force sync with cloud first to ensure project is saved in database
      const state = useProjectHistoryStore.getState();
      const activeProject = state.projects.find(p => p.id === activeProjectId);
      if (activeProject && activeProject.schema) {
        const syncData = [{
          id: activeProject.id,
          name: activeProject.name,
          dbType: activeProject.dbType,
          schemaJson: JSON.stringify(activeProject.schema),
          nodePositionsJson: JSON.stringify(activeProject.nodePositions ?? {}),
        }];
        await authService.syncProjects(syncData);
      }

      const { token } = await authService.createShareLink(activeProjectId);
      const badgeUrl = `${API_ORIGIN}/api/share/badge/${token}`;
      const shareUrl = `${typeof window !== 'undefined' ? window.location.origin : ''}/share/${token}`;
      const markdown = `[![DBA Score](${badgeUrl})](${shareUrl})`;
      await navigator.clipboard.writeText(markdown);
      showToast('DBA badge Markdown copied to clipboard!', 'success');
    } catch {
      showToast('Failed to generate badge URL.', 'error');
    } finally {
      setIsCopyingBadge(false);
    }
  };

  const { analyzeNow } = useAIDba();
  const { checkAccess } = useAIGateway();
  const isAnalyzing = useDbaStore(state => state.isAnalyzing);

  const handleManualAnalyze = async () => {
    if (!checkAccess("AI DBA Analysis")) return;
    setIsAnalyzingLocal(true);
    try {
      await analyzeNow();
      showToast('AI DBA Analysis completed successfully!', 'success');
    } catch (err: any) {
      if (err?.response?.status === 429) {
        showToast('Daily AI limit reached! Please upgrade your plan for unlimited access.', 'warning');
      } else {
        showToast(`Analysis failed: ${err.message || 'Unknown error'}`, 'error');
      }
    } finally {
      setIsAnalyzingLocal(false);
    }
  };

  const handleAutoFix = async () => {
    if (!checkAccess("AI Auto-Fix")) return;
    if (!schema || issues.length === 0) return;
    setIsFixing(true);

    try {
      const issuesText = issues
        .map((iss, i) => `${i + 1}. [${iss.ruleId}] [Category: ${iss.category || 'Performance'}] ${iss.tableName}${iss.columnName ? '.' + iss.columnName : ''}: ${iss.message} (Suggestion: ${iss.suggestion})`)
        .join('\n');

      const prompt = `Automatically resolve the following performance, structural, security, and cloud cost issues identified in the DBA Analysis and optimize the database schema:\n${issuesText}\n\nPlease:\n1. Define all missing Primary Key fields,\n2. Limit unbounded fields like NVARCHAR(MAX) with reasonable lengths (FinOps optimization),\n3. Optimize FK relationships that require indexes,\n4. Create a structure where data encryption/masking is configured on the C# side to protect sensitive personal data (passwords, credit cards, national IDs, etc.),\n5. Transform the schema into a flawless and secure architecture complying with enterprise standards.`;

      const revisedSchema = await schemaService.reviseSchema(
        schema.tables,
        schema.relations || [],
        prompt,
        aiProvider,
        modelName
      );

      loadFromSchema(revisedSchema, undefined, true);
      showToast('AI successfully resolved all DBA, Security, and FinOps issues and optimized your schema!', 'success');
    } catch (err: any) {
      if (err?.response?.status === 429) {
        showToast('Daily AI limit reached! Please upgrade your plan for unlimited access.', 'warning');
      } else {
        console.error('AI Auto-Fix error:', err);
        showToast(`Error: AI encountered an error while optimizing the schema: ${err.message}`, 'error');
      }
    } finally {
      setIsFixing(false);
    }
  };


  // Auto-scroll to the first issue of the selected table when drawer opens or filter changes
  React.useEffect(() => {
    if (isOpen && selectedTableFilter) {
      setTimeout(() => {
        const element = document.querySelector(`[data-table-issue="${selectedTableFilter}"]`);
        if (element) {
          element.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
      }, 300);
    }
  }, [isOpen, selectedTableFilter]);

  if (!isOpen) return null;

  // Filter issue list
  const filteredIssues = issues.filter(issue => {
    // Robust severity mapping (handles both C# serialised strings and integers)
    let severityNum = 0;
    const rawSeverity = issue.severity as any;
    if (typeof rawSeverity === 'number') {
      severityNum = rawSeverity;
    } else if (typeof rawSeverity === 'string') {
      const lower = rawSeverity.toLowerCase();
      if (lower === 'error' || lower === '2') severityNum = 2;
      else if (lower === 'warning' || lower === '1') severityNum = 1;
      else if (lower === 'info' || lower === '0') severityNum = 0;
    }

    // Severity Filter
    let matchesSeverity = true;
    if (filter === 'ERROR') matchesSeverity = severityNum === 2;
    else if (filter === 'WARNING') matchesSeverity = severityNum === 1;
    else if (filter === 'INFO') matchesSeverity = severityNum === 0;

    // Category Filter
    let matchesCategory = true;
    const itemCategory = issue.category || 'Performance';
    if (categoryFilter !== 'ALL') {
      matchesCategory = itemCategory.toLowerCase() === categoryFilter.toLowerCase();
    }

    return matchesSeverity && matchesCategory;
  });

  return (
    <div className="fixed top-14 right-0 bottom-0 z-[45] w-[400px] max-w-[92vw] bg-surface-800 border-l border-content-primary/10 shadow-[-4px_0_40px_rgba(0,0,0,0.5)] flex flex-col font-sans animate-in slide-in-from-right duration-250">

      {/* Header */}
      <div className="shrink-0 flex items-center justify-between px-5 py-3.5 border-b border-content-primary/10">
        <div className="flex items-center gap-2">
          <Shield className="w-4 h-4 text-content-muted" />
          <h3 className="text-sm font-bold text-content-primary flex items-center">
            Database Advisor
            <ContextualHelpTooltip content={helpContent.dbaAnalysis} />
          </h3>
        </div>
        <button
          onClick={onClose}
          className="p-1.5 rounded-lg text-content-subtle hover:text-content-primary hover:bg-white/[0.06] transition-all cursor-pointer"
          aria-label="Close DBA panel"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* Body Scroll area */}
      <div className="flex-1 min-h-0 overflow-y-auto p-4 space-y-4">

        {/* Score row */}
        <div className="flex items-center justify-between pb-4 border-b border-content-primary/8">
          <div className="space-y-1">
            <span className="text-[9px] text-content-subtle font-mono font-bold uppercase tracking-widest">
              Schema Score
            </span>
            <h4 className="text-sm font-bold text-content-primary tracking-tight">Database Health</h4>
          </div>

          <div className="relative w-14 h-14 flex items-center justify-center font-mono shrink-0">
            <svg className="w-full h-full transform -rotate-90">
              <circle cx="28" cy="28" r="23" className="stroke-surface-500 fill-none" strokeWidth="3.5" />
              <circle
                cx="28"
                cy="28"
                r="23"
                className="fill-none stroke-focus-ring"
                strokeWidth="3.5"
                strokeDasharray={`${2 * Math.PI * 23}`}
                strokeDashoffset={`${2 * Math.PI * 23 * (1 - score / 100)}`}
                strokeLinecap="round"
              />
            </svg>
            <div className="absolute flex items-center justify-center">
              <span className="text-sm font-bold text-content-primary">{score}</span>
            </div>
          </div>
        </div>

        {/* DBA Badge */}
        <button
          onClick={handleCopyBadge}
          disabled={isCopyingBadge}
          className="flex items-center gap-2 w-full px-3 py-2 rounded-lg bg-surface-700 hover:bg-surface-600 text-content-secondary hover:text-content-primary text-xs font-medium transition-all disabled:opacity-50 disabled:cursor-not-allowed"
          title="Copy Markdown badge for your README"
        >
          {isCopyingBadge
            ? <Loader2 className="w-3.5 h-3.5 animate-spin shrink-0" />
            : <Shield className="w-3.5 h-3.5 shrink-0" />
          }
          <span>Copy DBA Badge (Markdown)</span>
        </button>

        {/* Assessment */}
        <div className="space-y-1.5">
          <h4 className="text-[10px] font-bold text-content-subtle uppercase tracking-widest">Overall Assessment</h4>
          <p className="text-xs text-content-secondary leading-relaxed">{assessment}</p>
        </div>

        {/* Run AI DBA Analysis Button */}
        <button
          onClick={handleManualAnalyze}
          disabled={isAnalyzing || isAnalyzingLocal}
          className="w-full flex items-center justify-center gap-2 py-2.5 bg-surface-700 hover:bg-surface-600 disabled:opacity-50 text-content-secondary hover:text-content-primary font-semibold text-xs rounded-lg border border-content-primary/10 transition-all cursor-pointer"
        >
          {isAnalyzing || isAnalyzingLocal ? (
            <>
              <Loader2 className="w-4 h-4 animate-spin" />
              <span>Analyzing Schema...</span>
            </>
          ) : (
            <span>Run AI DBA Analysis</span>
          )}
        </button>

        {/* AI Auto-Fix Button — tek vurgu rengi, ana CTA */}
        {issues.length > 0 && (
          <button
            onClick={handleAutoFix}
            disabled={isFixing}
            className="w-full flex items-center justify-center gap-2 py-2.5 bg-content-primary hover:bg-content-primary-hover disabled:opacity-50 text-surface-900 font-semibold text-xs rounded-lg transition-all cursor-pointer"
          >
            {isFixing ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Fixing Issues with AI...</span>
              </>
            ) : (
              <span>Fix All Issues with AI</span>
            )}
          </button>
        )}

        {/* Category Tabs */}
        <div className="space-y-1.5">
          <h4 className="text-[10px] font-bold text-content-subtle uppercase tracking-widest">Enterprise Advisory</h4>

          <div className="grid grid-cols-4 gap-1 p-1 bg-surface-700 rounded-lg">
            <button
              onClick={() => setCategoryFilter('ALL')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'ALL'
                  ? 'bg-white/[0.08] border-white/25 text-content-primary'
                  : 'bg-transparent border-transparent text-content-subtle hover:text-content-primary'
              }`}
            >
              All ({issues.length})
            </button>
            <button
              onClick={() => setCategoryFilter('Performance')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'Performance'
                  ? 'bg-white/[0.08] border-white/25 text-content-primary'
                  : 'bg-transparent border-transparent text-content-subtle hover:text-content-primary'
              }`}
            >
              Perf ({issues.filter(i => (i.category || 'Performance').toLowerCase() === 'performance').length})
            </button>
            <button
              onClick={() => setCategoryFilter('Security')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'Security'
                  ? 'bg-white/[0.08] border-white/25 text-content-primary'
                  : 'bg-transparent border-transparent text-content-subtle hover:text-content-primary'
              }`}
            >
              Security ({issues.filter(i => (i.category || '').toLowerCase() === 'security').length})
            </button>
            <button
              onClick={() => setCategoryFilter('FinOps')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'FinOps'
                  ? 'bg-white/[0.08] border-white/25 text-content-primary'
                  : 'bg-transparent border-transparent text-content-subtle hover:text-content-primary'
              }`}
            >
              FinOps ({issues.filter(i => (i.category || '').toLowerCase() === 'finops').length})
            </button>
          </div>
        </div>

        {/* Filter Tab bar */}
        <div className="space-y-1.5">
          <h4 className="text-[10px] font-bold text-content-subtle uppercase tracking-widest">Severity Level ({filteredIssues.length})</h4>

          <div className="flex bg-surface-700 p-1 rounded-lg justify-between">
            {(['ALL', 'ERROR', 'WARNING', 'INFO'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setFilter(tab)}
                className={`
                  flex-1 py-1.5 text-[10px] font-bold rounded-lg cursor-pointer transition-all border duration-200
                  ${filter === tab
                    ? 'bg-white/[0.08] border-white/25 text-content-primary'
                    : 'bg-transparent border-transparent text-content-subtle hover:text-content-primary'
                  }
                `}
              >
                {tab === 'ALL' ? 'All' : tab === 'ERROR' ? 'Error' : tab === 'WARNING' ? 'Warning' : 'Info'}
              </button>
            ))}
          </div>
        </div>

        {/* Issues List */}
        <div className="space-y-3">
          {selectedTableFilter && (
            <div className="flex items-center justify-between bg-white/[0.08] border border-content-primary/10 px-4 py-3 rounded-xl animate-in fade-in duration-300">
              <div className="flex items-center gap-2">
                <span className="text-[10px] text-content-muted font-bold uppercase tracking-wider">Table:</span>
                <span className="text-[10px] font-mono font-bold text-content-primary bg-white/[0.08] border border-white/20 px-2 py-0.5 rounded">
                  {selectedTableFilter}
                </span>
              </div>
              <button
                onClick={() => setSelectedTableFilter(null)}
                className="text-[10px] text-content-muted hover:text-content-primary hover:underline underline-offset-2 transition-all cursor-pointer font-bold"
              >
                Clear
              </button>
            </div>
          )}

          {filteredIssues.length === 0 ? (
            <div className="text-center py-10 border border-dashed border-content-primary/12 rounded-xl bg-surface-700/40">
              <span className="text-content-muted text-xs font-semibold">No issues found. Congratulations!</span>
            </div>
          ) : (
            filteredIssues.map((issue, idx) => {
              let severityNum = 0;
              const rawSeverity = issue.severity as any;
              if (typeof rawSeverity === 'number') {
                severityNum = rawSeverity;
              } else if (typeof rawSeverity === 'string') {
                const lower = rawSeverity.toLowerCase();
                if (lower === 'error' || lower === '2') severityNum = 2;
                else if (lower === 'warning' || lower === '1') severityNum = 1;
                else if (lower === 'info' || lower === '0') severityNum = 0;
              }

              const Icon =
                severityNum === 2 ? AlertOctagon :
                severityNum === 1 ? AlertTriangle : Info;

              // Sarı/hardal aile kaldırıldı — yalnızca hata kırmızısı semantik
              // renk taşıyor, Warning/Info nötr off-white.
              const badgeColorClass =
                severityNum === 2 ? 'text-danger-text bg-danger-subtle border-danger/30' :
                severityNum === 1 ? 'text-content-secondary bg-surface-600 border-content-primary/15' :
                'text-content-muted bg-surface-700 border-content-primary/12';

              const isHighlighted = selectedTableFilter && issue.tableName === selectedTableFilter;
              const categoryLower = (issue.category || 'Performance').toLowerCase();

              let highlightBorderClass = '';
              if (isHighlighted) {
                if (categoryLower === 'security') {
                  highlightBorderClass = 'border-white/25 bg-white/[0.08] ring-1 ring-white/20';
                } else if (categoryLower === 'finops') {
                  highlightBorderClass = 'border-success bg-success-subtle ring-1 ring-success/40';
                } else {
                  highlightBorderClass = 'border-white/25 bg-white/[0.08] ring-1 ring-accent-hover/40';
                }
              } else {
                if (categoryLower === 'security') {
                  highlightBorderClass = 'border-content-primary/12 bg-surface-700/70 hover:border-content-primary/20';
                } else if (categoryLower === 'finops') {
                  highlightBorderClass = 'border-success/25 bg-success-subtle/60 hover:border-success/50';
                } else {
                  highlightBorderClass = 'border-content-primary/10 bg-surface-700/70 hover:border-content-primary/15';
                }
              }

              return (
                <div
                  key={idx}
                  data-table-issue={issue.tableName}
                  className={`p-3.5 rounded-xl flex flex-col gap-3 relative overflow-hidden transition-all border ${highlightBorderClass}`}
                >
                  {/* Badge Row */}
                  <div className="flex justify-between items-center shrink-0">
                    <div className="flex items-center gap-2">
                      <span className={`text-[9px] font-mono font-extrabold px-2 py-0.5 rounded border uppercase tracking-wider ${badgeColorClass}`}>
                        {issue.ruleId}
                      </span>
                      {categoryLower === 'security' && (
                        <span className="text-[9px] font-extrabold text-content-secondary bg-surface-600 border border-content-primary/15 px-2 py-0.5 rounded uppercase tracking-wider flex items-center gap-1">
                          Privacy
                        </span>
                      )}
                      {categoryLower === 'finops' && (
                        <span className="text-[9px] font-extrabold text-success-text bg-success-subtle border border-success/30 px-2 py-0.5 rounded uppercase tracking-wider flex items-center gap-1">
                          FinOps
                        </span>
                      )}
                      {isHighlighted && (
                        <span className="text-[9px] text-content-primary font-bold bg-white/[0.08] border border-white/15 px-1.5 py-0.5 rounded uppercase tracking-widest flex items-center gap-1">
                          <Sparkles className="w-2.5 h-2.5 text-content-primary" />
                          Selected
                        </span>
                      )}
                    </div>
                    <span className="text-[9px] font-extrabold text-content-subtle bg-surface-700 px-2 py-0.5 rounded border border-content-primary/10 uppercase tracking-widest flex items-center gap-1">
                      {issue.source === 'AI' ? (
                        <>
                          <Cpu className="w-3 h-3 text-content-primary" />
                          AI
                        </>
                      ) : (
                        'LOCAL'
                      )}
                    </span>
                  </div>

                  {/* Context headers */}
                  <div className="text-[11px] font-semibold text-content-primary bg-surface-700 px-2.5 py-1.5 rounded-lg border border-content-primary/10 flex items-center gap-2">
                    <span className="text-content-subtle font-bold">Table:</span>
                    <span className="text-content-primary font-mono font-bold">{issue.tableName}</span>
                    {issue.columnName && (
                      <>
                        <div className="w-1 h-1 rounded-full bg-surface-500/40" />
                        <span className="text-content-subtle font-bold">Column:</span>
                        <span className="text-content-primary font-mono font-bold">{issue.columnName}</span>
                      </>
                    )}
                  </div>

                  {/* Message & Suggestion details */}
                  <div className="space-y-2">
                    <div className="flex items-start gap-2.5">
                       <Icon className={`w-4 h-4 shrink-0 mt-0.5 ${
                        severityNum === 2 ? 'text-danger-text' :
                        severityNum === 1 ? 'text-content-secondary' : 'text-content-muted'
                      }`} />
                      <p className="text-[12px] text-content-primary leading-relaxed font-semibold">
                        {issue.message}
                      </p>
                    </div>

                    {issue.suggestion && (
                      <div className="bg-surface-700 border border-content-primary/10 p-3 rounded-lg flex flex-col gap-1 text-[11px]">
                        <span className="font-extrabold text-content-muted uppercase tracking-wider text-[9px] mb-0.5">
                          Resolution Recommendation
                        </span>
                        <p className="text-content-primary leading-relaxed font-medium">
                          {issue.suggestion}
                        </p>
                      </div>
                    )}
                  </div>
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
