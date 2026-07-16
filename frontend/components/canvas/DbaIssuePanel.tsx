import React, { useState } from 'react';
import {
  X, AlertTriangle, Award, ShieldAlert, Cpu, Sparkles, Loader2,
  ShieldCheck, Cloud, AlertOctagon, Info
} from 'lucide-react';
import { DbaIssue, useAIDba } from '../../hooks/useAIDba';
import { useAIGateway } from '../../hooks/useAIGateway';
import { useDbaStore } from '../../store/useDbaStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { schemaService } from '../../services/api';
import ContextualHelpTooltip from '../help/ContextualHelpTooltip';
import { helpContent } from '../../lib/helpContent';

interface DbaIssuePanelProps {
  isOpen: boolean;
  onClose: () => void;
  issues: DbaIssue[];
  score: number;
  assessment: string;
}

export default function DbaIssuePanel({ isOpen, onClose, issues, score, assessment }: DbaIssuePanelProps) {
  const [filter, setFilter] = useState<'ALL' | 'ERROR' | 'WARNING' | 'INFO'>('ALL');
  const [categoryFilter, setCategoryFilter] = useState<'ALL' | 'Performance' | 'Security' | 'FinOps'>('ALL');
  const selectedTableFilter = useDbaStore(state => state.selectedTableFilter);
  const setSelectedTableFilter = useDbaStore(state => state.setSelectedTableFilter);
  
  const { schema, loadFromSchema, aiProvider, modelName } = useSchemaStore();
  const [isFixing, setIsFixing] = useState(false);
  const [isAnalyzingLocal, setIsAnalyzingLocal] = useState(false);
  const showToast = useToastStore(state => state.showToast);

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
    <div className="fixed top-[72px] right-6 bottom-6 z-[45] w-96 bg-surface-900/95 backdrop-blur-2xl border border-indigo-500/30 shadow-[0_10px_50px_rgba(0,0,0,0.85)] flex flex-col font-sans transition-all duration-300 transform translate-x-0 overflow-hidden rounded-2xl">
      
      {/* Header - Styled to match the Celestial Dashboard perfectly */}
      <div className="shrink-0 flex items-center justify-between px-5 py-4 border-b border-indigo-500/20 bg-[#0B1120]/90">
        <div className="flex items-center gap-2">
          <svg className="w-4.5 h-4.5 text-indigo-400 animate-pulse shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 2L20 6.5V17.5L12 22L4 17.5V6.5L12 2Z" className="opacity-80" />
            <circle cx="12" cy="12" r="3" className="fill-current text-cyan-400" />
            <path d="M12 2v7M12 15v7M4 6.5l8 5.5M20 6.5l-8 5.5M4 17.5l8-5.5M20 17.5l-8-5.5" className="opacity-60 text-indigo-300" />
          </svg>
          <h3 className="text-sm font-extrabold text-indigo-100 uppercase tracking-wider flex items-center">
            DBA
            <ContextualHelpTooltip content={helpContent.dbaAnalysis} />
          </h3>
        </div>
        <button
          onClick={onClose}
          className="p-1 rounded-lg text-zinc-400 hover:text-white hover:bg-white/5 transition-all cursor-pointer"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      {/* Body Scroll area */}
      <div className="flex-1 min-h-0 overflow-y-auto p-5 space-y-6 scrollbar-thin">
        
        {/* Score Card - Gradient glowing circular progress bar matching Stitch! */}
        <div className="bg-[#0B1120]/80 border border-indigo-500/20 rounded-2xl p-5 flex items-center justify-between shadow-[0_0_25px_rgba(99,102,241,0.12)] relative overflow-hidden">
          <div className="absolute -top-10 -left-10 w-24 h-24 bg-indigo-500/5 rounded-full blur-2xl pointer-events-none" />
          
          <div className="space-y-1.5 max-w-[180px]">
            <span className="text-[9px] text-indigo-400 font-mono font-extrabold uppercase tracking-widest">
              SCHEMA SCORE
            </span>
            <h4 className="text-md font-bold text-zinc-100 tracking-tight leading-tight">Database Health</h4>
            <p className="text-[10px] text-zinc-400 font-semibold leading-normal">Score is calculated out of 100.</p>
          </div>

          {/* Radial score circle with glow and gradient stroke */}
          <div className="relative w-18 h-18 flex items-center justify-center font-mono shrink-0">
            <svg className="w-full h-full transform -rotate-90">
              <defs>
                <linearGradient id="dba-score-grad" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stopColor="#06b6d4" />     {/* Cyan */}
                  <stop offset="50%" stopColor="#6366f1" />    {/* Indigo */}
                  <stop offset="100%" stopColor="#a855f7" />   {/* Purple */}
                </linearGradient>
              </defs>
              <circle
                cx="36"
                cy="36"
                r="30"
                className="stroke-zinc-800/80 fill-none"
                strokeWidth="4.5"
              />
              <circle
                cx="36"
                cy="36"
                r="30"
                className="fill-none drop-shadow-[0_0_6px_rgba(99,102,241,0.6)]"
                stroke="url(#dba-score-grad)"
                strokeWidth="4.5"
                strokeDasharray={`${2 * Math.PI * 30}`}
                strokeDashoffset={`${2 * Math.PI * 30 * (1 - score / 100)}`}
                strokeLinecap="round"
              />
            </svg>
            <div className="absolute flex items-center justify-center">
              <span className="text-base font-extrabold text-white">
                {score}
              </span>
            </div>
          </div>
        </div>

        {/* Assessment Section - With waving vector horizontal divider */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="text-[10px] font-bold text-indigo-300 uppercase tracking-widest flex items-center gap-1.5 shrink-0">
              <Award className="w-4 h-4 text-indigo-400" />
              Overall Assessment
            </h4>
            {/* Wavy line graphic */}
            <div className="flex-1 h-px bg-gradient-to-r from-indigo-500/30 via-purple-500/10 to-transparent ml-3 relative overflow-hidden">
              <svg className="absolute inset-0 w-full h-[5px] text-indigo-500/30" viewBox="0 0 100 5" preserveAspectRatio="none">
                <path d="M0,2.5 C25,0 25,5 50,2.5 C75,0 75,5 100,2.5" fill="none" stroke="currentColor" strokeWidth="0.8" />
              </svg>
            </div>
          </div>
          
          <div className="bg-[#0B1120]/40 border border-indigo-500/10 p-4 rounded-2xl text-xs text-zinc-300 leading-relaxed font-semibold">
            {assessment}
          </div>
        </div>

        {/* Run AI DBA Analysis Button */}
        <button
          onClick={handleManualAnalyze}
          disabled={isAnalyzing || isAnalyzingLocal}
          className="w-full relative group flex items-center justify-center gap-2 py-3.5 bg-gradient-to-r from-purple-900 via-indigo-950 to-purple-900 hover:from-purple-850 hover:to-indigo-900 disabled:from-zinc-800 disabled:to-zinc-900 text-zinc-200 hover:text-white font-extrabold text-xs tracking-wider uppercase rounded-2xl border border-purple-500/20 shadow-md hover:scale-[1.01] active:scale-[0.99] transition-all duration-300 cursor-pointer"
        >
          {isAnalyzing || isAnalyzingLocal ? (
            <>
              <Loader2 className="w-4 h-4 animate-spin text-indigo-400" />
              <span>Analyzing Schema...</span>
            </>
          ) : (
            <span>Run AI DBA Analysis</span>
          )}
        </button>

        {/* AI Auto-Fix Button - Matching purple background & sparkles exactly */}
        {issues.length > 0 && (
          <button
            onClick={handleAutoFix}
            disabled={isFixing}
            className="w-full relative group flex items-center justify-center gap-2 py-3.5 bg-gradient-to-r from-violet-600 to-indigo-600 hover:from-violet-500 hover:to-indigo-500 disabled:from-indigo-800 disabled:to-purple-800 text-white font-extrabold text-xs tracking-wider uppercase rounded-2xl shadow-[0_4px_20px_rgba(139,92,246,0.35)] hover:shadow-[0_4px_25px_rgba(139,92,246,0.55)] hover:scale-[1.02] active:scale-[0.98] transition-all duration-300 border border-violet-400/20 overflow-hidden cursor-pointer"
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

        {/* EnterpriseAdvisor Category Tabs */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="text-[10px] font-bold text-indigo-300 uppercase tracking-widest flex items-center gap-1.5 shrink-0">
              <ShieldCheck className="w-4 h-4 text-indigo-400" />
              Enterprise Advisory
            </h4>
            {/* Wavy line graphic */}
            <div className="flex-1 h-px bg-gradient-to-r from-indigo-500/30 via-purple-500/10 to-transparent ml-3 relative overflow-hidden">
              <svg className="absolute inset-0 w-full h-[5px] text-indigo-500/30" viewBox="0 0 100 5" preserveAspectRatio="none">
                <path d="M0,2.5 C25,0 25,5 50,2.5 C75,0 75,5 100,2.5" fill="none" stroke="currentColor" strokeWidth="0.8" />
              </svg>
            </div>
          </div>

          <div className="grid grid-cols-4 gap-1 p-1 bg-zinc-950/70 border border-zinc-800/60 rounded-xl">
            <button
              onClick={() => setCategoryFilter('ALL')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'ALL'
                  ? 'bg-zinc-800 border-zinc-700/80 text-white shadow-[0_2px_8px_rgba(0,0,0,0.4)]'
                  : 'bg-transparent border-transparent text-zinc-500 hover:text-zinc-300'
              }`}
            >
              All ({issues.length})
            </button>
            <button
              onClick={() => setCategoryFilter('Performance')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'Performance'
                  ? 'bg-zinc-800 border-zinc-700/80 text-white shadow-[0_2px_8px_rgba(0,0,0,0.4)]'
                  : 'bg-transparent border-transparent text-zinc-500 hover:text-zinc-300'
              }`}
            >
              Perf ({issues.filter(i => (i.category || 'Performance').toLowerCase() === 'performance').length})
            </button>
            <button
              onClick={() => setCategoryFilter('Security')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'Security'
                  ? 'bg-zinc-800 border-zinc-700/80 text-white shadow-[0_2px_8px_rgba(0,0,0,0.4)]'
                  : 'bg-transparent border-transparent text-zinc-500 hover:text-zinc-300'
              }`}
            >
              Security ({issues.filter(i => (i.category || '').toLowerCase() === 'security').length})
            </button>
            <button
              onClick={() => setCategoryFilter('FinOps')}
              className={`py-1.5 text-[9px] font-extrabold rounded-lg cursor-pointer transition-all duration-200 border ${
                categoryFilter === 'FinOps'
                  ? 'bg-zinc-800 border-zinc-700/80 text-white shadow-[0_2px_8px_rgba(0,0,0,0.4)]'
                  : 'bg-transparent border-transparent text-zinc-500 hover:text-zinc-300'
              }`}
            >
              FinOps ({issues.filter(i => (i.category || '').toLowerCase() === 'finops').length})
            </button>
          </div>
        </div>

        {/* Filter Tab bar */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="text-[10px] font-bold text-indigo-300 uppercase tracking-widest flex items-center gap-1.5 shrink-0">
              <ShieldAlert className="w-4 h-4 text-indigo-400" />
              Severity Level ({filteredIssues.length})
            </h4>
            {/* Wavy line graphic */}
            <div className="flex-1 h-px bg-gradient-to-r from-indigo-500/30 via-purple-500/10 to-transparent ml-3 relative overflow-hidden">
              <svg className="absolute inset-0 w-full h-[5px] text-indigo-500/30" viewBox="0 0 100 5" preserveAspectRatio="none">
                <path d="M0,2.5 C25,0 25,5 50,2.5 C75,0 75,5 100,2.5" fill="none" stroke="currentColor" strokeWidth="0.8" />
              </svg>
            </div>
          </div>

          <div className="flex bg-zinc-950/70 p-1 border border-zinc-800/60 rounded-xl justify-between">
            {(['ALL', 'ERROR', 'WARNING', 'INFO'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setFilter(tab)}
                className={`
                  flex-1 py-1.5 text-[10px] font-bold rounded-lg cursor-pointer transition-all border duration-200
                  ${filter === tab
                    ? 'bg-zinc-800 border-zinc-700/80 text-white shadow-[0_2px_8px_rgba(0,0,0,0.4)]'
                    : 'bg-transparent border-transparent text-zinc-500 hover:text-zinc-300'
                  }
                `}
              >
                {tab === 'ALL' ? 'All' : tab === 'ERROR' ? 'Error' : tab === 'WARNING' ? 'Warning' : 'Info'}
              </button>
            ))}
          </div>
        </div>

        {/* Issues List */}
        <div className="space-y-4">
          {selectedTableFilter && (
            <div className="flex items-center justify-between bg-indigo-950/20 border border-indigo-900/40 px-4 py-3 rounded-xl shadow-md animate-in fade-in duration-300">
              <div className="flex items-center gap-2">
                <span className="text-[10px] text-indigo-400 font-bold uppercase tracking-wider">Table:</span>
                <span className="text-[10px] font-mono font-bold text-zinc-100 bg-indigo-900/50 border border-indigo-700 px-2 py-0.5 rounded shadow-[0_0_10px_rgba(99,102,241,0.2)]">
                  {selectedTableFilter}
                </span>
              </div>
              <button
                onClick={() => setSelectedTableFilter(null)}
                className="text-[10px] text-zinc-400 hover:text-white hover:underline underline-offset-2 transition-all cursor-pointer font-bold"
              >
                Clear
              </button>
            </div>
          )}

          {filteredIssues.length === 0 ? (
            <div className="text-center py-10 border border-dashed border-zinc-800 rounded-2xl bg-zinc-900/10">
              <span className="text-zinc-500 text-xs font-semibold">No issues found. Congratulations!</span>
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

              const badgeColorClass =
                severityNum === 2 ? 'text-red-400 bg-red-950/20 border-red-900/30' :
                severityNum === 1 ? 'text-amber-400 bg-amber-950/20 border-amber-900/30' :
                'text-sky-400 bg-sky-950/20 border-sky-900/30';

              const isHighlighted = selectedTableFilter && issue.tableName === selectedTableFilter;
              const categoryLower = (issue.category || 'Performance').toLowerCase();
              
              let highlightBorderClass = '';
              if (isHighlighted) {
                if (categoryLower === 'security') {
                  highlightBorderClass = 'border-amber-500 bg-amber-950/25 ring-1 ring-amber-500/40 shadow-[0_0_20px_rgba(245,158,11,0.2)] animate-pulse';
                } else if (categoryLower === 'finops') {
                  highlightBorderClass = 'border-emerald-500 bg-emerald-950/25 ring-1 ring-emerald-500/40 shadow-[0_0_20px_rgba(16,185,129,0.2)] animate-pulse';
                } else {
                  highlightBorderClass = 'border-indigo-500/80 bg-indigo-950/20 ring-1 ring-indigo-500/30 shadow-[0_0_20px_rgba(99,102,241,0.15)] animate-pulse';
                }
              } else {
                if (categoryLower === 'security') {
                  highlightBorderClass = 'border-amber-900/40 bg-amber-950/10 hover:border-amber-800/60 shadow-[0_0_15px_rgba(245,158,11,0.05)]';
                } else if (categoryLower === 'finops') {
                  highlightBorderClass = 'border-emerald-900/40 bg-emerald-950/10 hover:border-emerald-800/60 shadow-[0_0_15px_rgba(16,185,129,0.05)]';
                } else {
                  highlightBorderClass = 'border-zinc-800 bg-[#111827]/70 hover:border-zinc-700';
                }
              }

              return (
                <div
                  key={idx}
                  data-table-issue={issue.tableName}
                  className={`p-4 rounded-2xl flex flex-col gap-3.5 shadow-md relative overflow-hidden transition-all border ${highlightBorderClass}`}
                >
                  {/* Badge Row */}
                  <div className="flex justify-between items-center shrink-0">
                    <div className="flex items-center gap-2">
                      <span className={`text-[9px] font-mono font-extrabold px-2 py-0.5 rounded border uppercase tracking-wider ${badgeColorClass}`}>
                        {issue.ruleId}
                      </span>
                      {categoryLower === 'security' && (
                        <span className="text-[9px] font-extrabold text-amber-400 bg-amber-950/50 border border-amber-500/20 px-2 py-0.5 rounded uppercase tracking-wider flex items-center gap-1">
                          Privacy
                        </span>
                      )}
                      {categoryLower === 'finops' && (
                        <span className="text-[9px] font-extrabold text-emerald-400 bg-emerald-950/50 border border-emerald-500/20 px-2 py-0.5 rounded uppercase tracking-wider flex items-center gap-1">
                          FinOps
                        </span>
                      )}
                      {isHighlighted && (
                        <span className="text-[9px] text-indigo-400 font-bold bg-indigo-950/50 border border-indigo-500/20 px-1.5 py-0.5 rounded uppercase tracking-widest flex items-center gap-1">
                          <Sparkles className="w-2.5 h-2.5 text-indigo-400 animate-spin" />
                          Selected
                        </span>
                      )}
                    </div>
                    <span className="text-[9px] font-extrabold text-zinc-500 bg-zinc-950/60 px-2 py-0.5 rounded border border-zinc-800 uppercase tracking-widest flex items-center gap-1">
                      {issue.source === 'AI' ? (
                        <>
                          <Cpu className="w-3 h-3 text-indigo-400 animate-pulse" />
                          AI
                        </>
                      ) : (
                        'LOCAL'
                      )}
                    </span>
                  </div>

                  {/* Context headers */}
                  <div className="text-[11px] font-semibold text-zinc-300 bg-zinc-950/80 px-2.5 py-1.5 rounded-xl border border-zinc-850 flex items-center gap-2">
                    <span className="text-zinc-500 font-bold">Table:</span>
                    <span className="text-zinc-200 font-mono font-bold">{issue.tableName}</span>
                    {issue.columnName && (
                      <>
                        <div className="w-1 h-1 rounded-full bg-zinc-700" />
                        <span className="text-zinc-500 font-bold">Column:</span>
                        <span className="text-zinc-200 font-mono font-bold">{issue.columnName}</span>
                      </>
                    )}
                  </div>

                  {/* Message & Suggestion details */}
                  <div className="space-y-2">
                    <div className="flex items-start gap-2.5">
                       <Icon className={`w-4 h-4 shrink-0 mt-0.5 ${
                        severityNum === 2 ? 'text-red-400' :
                        severityNum === 1 ? 'text-amber-400' : 'text-sky-400'
                      }`} />
                      <p className="text-[12px] text-zinc-200 leading-relaxed font-semibold">
                        {issue.message}
                      </p>
                    </div>

                    {issue.suggestion && (
                      <div className="bg-zinc-950/60 border border-zinc-800/80 p-3 rounded-xl flex flex-col gap-1 text-[11px]">
                        <span className="font-extrabold text-zinc-400 uppercase tracking-wider text-[9px] mb-0.5">
                          Resolution Recommendation
                        </span>
                        <p className="text-zinc-300 leading-relaxed font-medium">
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
