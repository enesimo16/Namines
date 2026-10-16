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
import { apiErrorStatus, errorMessage } from '../../lib/errors';

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

  const { schema, loadFromSchema, naiModel } = useSchemaStore();
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
    } catch (err) {
      if (apiErrorStatus(err) === 429) {
        showToast('Daily AI limit reached! Please upgrade your plan for unlimited access.', 'warning');
      } else {
        showToast(`Analysis failed: ${errorMessage(err, 'Unknown error')}`, 'error');
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
        naiModel
      );

      loadFromSchema(revisedSchema, undefined, true);
      showToast('AI successfully resolved all DBA, Security, and FinOps issues and optimized your schema!', 'success');
    } catch (err) {
      if (apiErrorStatus(err) === 429) {
        showToast('Daily AI limit reached! Please upgrade your plan for unlimited access.', 'warning');
      } else {
        console.error('AI Auto-Fix error:', err);
        showToast(`Error: AI encountered an error while optimizing the schema: ${errorMessage(err, 'unknown error')}`, 'error');
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
    // Sunucu bu alani hem sayi hem metin olarak gonderebiliyor (C#
    // serilestirmesi surume gore degisiyor); tip bunu ACIKCA soyluyor.
    const rawSeverity = issue.severity as number | string;
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
    <div className="fixed top-14 right-0 bottom-0 z-drawer w-[400px] max-w-[92vw] bg-surface-800 border-l border-line shadow-2xl flex flex-col font-sans animate-in slide-in-from-right duration-250">

      {/* Header */}
      <div className="shrink-0 flex items-center justify-between px-5 py-3.5 border-b border-line">
        <div className="flex items-center gap-2">
          <Shield className="w-4 h-4 text-content-muted" />
          <h3 className="text-sm font-bold text-content-primary flex items-center">
            Database Advisor
            <ContextualHelpTooltip content={helpContent.dbaAnalysis} />
          </h3>
        </div>
        <button
          onClick={onClose}
          className="p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700/60 transition-colors cursor-pointer"
          aria-label="Close DBA panel"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* Body Scroll area */}
      <div className="flex-1 min-h-0 overflow-y-auto p-4 space-y-4">

        {/* Polaris Metric Card: Database Health & Score */}
        <div className="bg-surface-700/40 border border-line rounded-[var(--radius-card)] p-4 flex items-center justify-between shadow-xs">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <span className="text-micro text-content-muted font-mono font-bold uppercase tracking-wider">
                Database Health
              </span>
              <span className={`text-[10px] font-bold px-2 py-0.5 rounded-full border ${
                score >= 90
                  ? 'bg-success-subtle text-success-text border-success/30'
                  : score >= 70
                    ? 'bg-surface-600 text-content-primary border-line-strong'
                    : 'bg-danger-subtle text-danger-text border-danger/30'
              }`}>
                {score >= 90 ? 'Optimal' : score >= 70 ? 'Fair' : 'Needs Review'}
              </span>
            </div>
            <div className="text-2xl font-bold text-content-primary tracking-tight font-mono">
              {score}<span className="text-sm font-normal text-content-muted font-sans">/100</span>
            </div>
          </div>

          <div className="relative w-14 h-14 flex items-center justify-center font-mono shrink-0">
            <svg className="w-full h-full transform -rotate-90">
              <circle cx="28" cy="28" r="23" className="stroke-surface-600 fill-none" strokeWidth="4" />
              <circle
                cx="28"
                cy="28"
                r="23"
                className={`fill-none ${score >= 90 ? 'stroke-success' : score >= 70 ? 'stroke-content-primary' : 'stroke-danger'}`}
                strokeWidth="4"
                strokeDasharray={`${2 * Math.PI * 23}`}
                strokeDashoffset={`${2 * Math.PI * 23 * (1 - score / 100)}`}
                strokeLinecap="round"
              />
            </svg>
            <div className="absolute flex items-center justify-center">
              <span className="text-xs font-bold text-content-primary">{score}%</span>
            </div>
          </div>
        </div>

        {/* DBA Badge */}
        <button
          onClick={handleCopyBadge}
          disabled={isCopyingBadge}
          className="flex items-center justify-center gap-2 w-full px-3 py-2 rounded-[var(--radius-control)] border border-line bg-surface-700/50 hover:bg-surface-700 hover:border-line-strong text-content-secondary hover:text-content-primary text-xs font-medium transition-all shadow-xs disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer"
          title="Copy Markdown badge for your README"
        >
          {isCopyingBadge
            ? <Loader2 className="w-3.5 h-3.5 animate-spin shrink-0" />
            : <Shield className="w-3.5 h-3.5 shrink-0" />
          }
          <span>Copy DBA Badge (Markdown)</span>
        </button>

        {/* Assessment */}
        <div className="p-3 bg-surface-700/30 border border-line rounded-[var(--radius-control)] space-y-1">
          <h4 className="text-[10px] font-bold text-content-muted uppercase tracking-wider">Overall Assessment</h4>
          <p className="text-xs text-content-secondary leading-relaxed">{assessment}</p>
        </div>

        {/* Run AI DBA Analysis Button */}
        <button
          onClick={handleManualAnalyze}
          disabled={isAnalyzing || isAnalyzingLocal}
          className="w-full flex items-center justify-center gap-2 py-2.5 bg-surface-700 hover:bg-surface-600 disabled:opacity-50 text-content-primary font-semibold text-xs rounded-[var(--radius-control)] border border-line-strong transition-all cursor-pointer shadow-xs"
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
            className="w-full flex items-center justify-center gap-2 py-2.5 bg-content-primary hover:bg-content-primary-hover disabled:opacity-50 text-surface-900 font-semibold text-xs rounded-[var(--radius-control)] shadow-sm transition-all cursor-pointer"
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
          <h4 className="text-[10px] font-bold text-content-muted uppercase tracking-wider">Enterprise Advisory</h4>

          <div className="grid grid-cols-4 gap-1 p-0.5 bg-surface-700/60 border border-line rounded-[var(--radius-control)]">
            <button
              onClick={() => setCategoryFilter('ALL')}
              className={`py-1.5 text-micro font-bold rounded-[calc(var(--radius-control)-2px)] cursor-pointer transition-all ${
                categoryFilter === 'ALL'
                  ? 'bg-surface-800 border border-line-strong text-content-primary shadow-xs'
                  : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 border border-transparent'
              }`}
            >
              All ({issues.length})
            </button>
            <button
              onClick={() => setCategoryFilter('Performance')}
              className={`py-1.5 text-micro font-bold rounded-[calc(var(--radius-control)-2px)] cursor-pointer transition-all ${
                categoryFilter === 'Performance'
                  ? 'bg-surface-800 border border-line-strong text-content-primary shadow-xs'
                  : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 border border-transparent'
              }`}
            >
              Perf ({issues.filter(i => (i.category || 'Performance').toLowerCase() === 'performance').length})
            </button>
            <button
              onClick={() => setCategoryFilter('Security')}
              className={`py-1.5 text-micro font-bold rounded-[calc(var(--radius-control)-2px)] cursor-pointer transition-all ${
                categoryFilter === 'Security'
                  ? 'bg-surface-800 border border-line-strong text-content-primary shadow-xs'
                  : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 border border-transparent'
              }`}
            >
              Security ({issues.filter(i => (i.category || '').toLowerCase() === 'security').length})
            </button>
            <button
              onClick={() => setCategoryFilter('FinOps')}
              className={`py-1.5 text-micro font-bold rounded-[calc(var(--radius-control)-2px)] cursor-pointer transition-all ${
                categoryFilter === 'FinOps'
                  ? 'bg-surface-800 border border-line-strong text-content-primary shadow-xs'
                  : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 border border-transparent'
              }`}
            >
              FinOps ({issues.filter(i => (i.category || '').toLowerCase() === 'finops').length})
            </button>
          </div>
        </div>

        {/* Filter Tab bar */}
        <div className="space-y-1.5">
          <h4 className="text-[10px] font-bold text-content-muted uppercase tracking-wider">Severity Level ({filteredIssues.length})</h4>

          <div className="flex bg-surface-700/60 border border-line p-0.5 rounded-[var(--radius-control)] gap-1">
            {(['ALL', 'ERROR', 'WARNING', 'INFO'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setFilter(tab)}
                className={`
                  flex-1 py-1.5 text-[10px] font-bold rounded-[calc(var(--radius-control)-2px)] cursor-pointer transition-all
                  ${filter === tab
                    ? 'bg-surface-800 border border-line-strong text-content-primary shadow-xs'
                    : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 border border-transparent'
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
            <div className="flex items-center justify-between bg-surface-700/50 border border-line px-3 py-2 rounded-[var(--radius-control)] animate-in fade-in duration-200">
              <div className="flex items-center gap-2">
                <span className="text-[10px] text-content-muted font-bold uppercase tracking-wider">Table:</span>
                <span className="text-[11px] font-mono font-bold text-content-primary bg-surface-800 border border-line-strong px-2 py-0.5 rounded-[var(--radius-control)]">
                  {selectedTableFilter}
                </span>
              </div>
              <button
                onClick={() => setSelectedTableFilter(null)}
                className="text-xs text-content-muted hover:text-content-primary transition-colors cursor-pointer font-medium"
              >
                Clear
              </button>
            </div>
          )}

          {filteredIssues.length === 0 ? (
            <div className="text-center py-10 border border-dashed border-line rounded-[var(--radius-card)] bg-surface-700/20">
              <span className="text-content-muted text-xs font-medium">No issues found. Schema is healthy!</span>
            </div>
          ) : (
            filteredIssues.map((issue, idx) => {
              let severityNum = 0;
              const rawSeverity = issue.severity as number | string;
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
                severityNum === 1 ? 'text-content-primary bg-surface-700 border-line-strong' :
                'text-content-muted bg-surface-700/60 border-line';

              const isHighlighted = selectedTableFilter && issue.tableName === selectedTableFilter;
              const categoryLower = (issue.category || 'Performance').toLowerCase();

              let highlightBorderClass = '';
              if (isHighlighted) {
                if (categoryLower === 'security') {
                  highlightBorderClass = 'border-line-strong bg-surface-700/70 ring-1 ring-line-strong';
                } else if (categoryLower === 'finops') {
                  highlightBorderClass = 'border-success bg-success-subtle ring-1 ring-success/40';
                } else {
                  highlightBorderClass = 'border-line-strong bg-surface-700/70 ring-1 ring-line-strong';
                }
              } else {
                if (categoryLower === 'security') {
                  highlightBorderClass = 'border-line bg-surface-800 hover:border-line-strong';
                } else if (categoryLower === 'finops') {
                  highlightBorderClass = 'border-success/30 bg-success-subtle/40 hover:border-success/50';
                } else {
                  highlightBorderClass = 'border-line bg-surface-800 hover:border-line-strong';
                }
              }

              return (
                <div
                  key={idx}
                  data-table-issue={issue.tableName}
                  className={`p-3.5 rounded-[var(--radius-card)] flex flex-col gap-2.5 relative overflow-hidden transition-all border shadow-xs ${highlightBorderClass}`}
                >
                  {/* Badge Row */}
                  <div className="flex justify-between items-center shrink-0">
                    <div className="flex items-center gap-1.5">
                      <span className={`text-micro font-mono font-bold px-2 py-0.5 rounded-full border uppercase tracking-wider ${badgeColorClass}`}>
                        {issue.ruleId}
                      </span>
                      {categoryLower === 'security' && (
                        <span className="text-micro font-semibold text-content-secondary bg-surface-700 border border-line px-2 py-0.5 rounded-full uppercase tracking-wider flex items-center gap-1">
                          Privacy
                        </span>
                      )}
                      {categoryLower === 'finops' && (
                        <span className="text-micro font-semibold text-success-text bg-success-subtle border border-success/30 px-2 py-0.5 rounded-full uppercase tracking-wider flex items-center gap-1">
                          FinOps
                        </span>
                      )}
                      {isHighlighted && (
                        <span className="text-micro text-content-primary font-bold bg-surface-700 border border-line-strong px-2 py-0.5 rounded-full uppercase tracking-widest flex items-center gap-1">
                          <Sparkles className="w-2.5 h-2.5 text-content-primary" />
                          Selected
                        </span>
                      )}
                    </div>
                    <span className="text-micro font-bold text-content-muted bg-surface-700/60 px-2 py-0.5 rounded-full border border-line uppercase tracking-widest flex items-center gap-1">
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
                  <div className="text-[11px] font-medium text-content-primary bg-surface-700/50 px-2.5 py-1.5 rounded-[var(--radius-control)] border border-line flex items-center gap-2">
                    <span className="text-content-muted font-medium">Table:</span>
                    <span className="text-content-primary font-mono font-semibold">{issue.tableName}</span>
                    {issue.columnName && (
                      <>
                        <div className="w-1 h-1 rounded-full bg-surface-500/40" />
                        <span className="text-content-muted font-medium">Column:</span>
                        <span className="text-content-primary font-mono font-semibold">{issue.columnName}</span>
                      </>
                    )}
                  </div>

                  {/* Message & Suggestion details */}
                  <div className="space-y-2">
                    <div className="flex items-start gap-2">
                       <Icon className={`w-4 h-4 shrink-0 mt-0.5 ${
                        severityNum === 2 ? 'text-danger-text' :
                        severityNum === 1 ? 'text-content-secondary' : 'text-content-muted'
                      }`} />
                      <p className="text-xs text-content-primary leading-relaxed font-medium">
                        {issue.message}
                      </p>
                    </div>

                    {issue.suggestion && (
                      <div className="bg-surface-700/40 border border-line p-2.5 rounded-[var(--radius-control)] flex flex-col gap-1 text-xs">
                        <span className="font-bold text-content-muted uppercase tracking-wider text-micro">
                          Recommendation
                        </span>
                        <p className="text-content-secondary leading-relaxed">
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
