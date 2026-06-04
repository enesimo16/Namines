import { Database, AlertTriangle, AlertCircle, HelpCircle, Loader2 } from 'lucide-react';
import { DbaIssue } from '../../hooks/useAIDba';

interface DbaScoreBarProps {
  score: number;
  issues: DbaIssue[];
  isAnalyzing: boolean;
  onTogglePanel: () => void;
}

export default function DbaScoreBar({ score, issues, isAnalyzing, onTogglePanel }: DbaScoreBarProps) {
  const errors = issues.filter(i => {
    const raw = i.severity as any;
    const s = typeof raw === 'string' ? raw.toLowerCase() : String(raw);
    return s === '2' || s === 'error';
  }).length;

  const warnings = issues.filter(i => {
    const raw = i.severity as any;
    const s = typeof raw === 'string' ? raw.toLowerCase() : String(raw);
    return s === '1' || s === 'warning';
  }).length;

  const infos = issues.filter(i => {
    const raw = i.severity as any;
    const s = typeof raw === 'string' ? raw.toLowerCase() : String(raw);
    return s === '0' || s === 'info';
  }).length;

  // Health color logic
  const scoreColorClass =
    score >= 90 ? 'text-emerald-400 bg-emerald-950/30 border-emerald-800/40' :
    score >= 70 ? 'text-amber-400 bg-amber-950/30 border-amber-800/40' :
    'text-red-400 bg-red-950/30 border-red-800/40';

  return (
    <div
      onClick={onTogglePanel}
      className={`
        fixed top-4 left-1/2 -translate-x-1/2 z-40
        flex items-center gap-5 px-5 py-3 rounded-2xl
        bg-zinc-900/90 backdrop-blur-md border border-zinc-800/60
        shadow-[0_4px_30px_rgba(0,0,0,0.4),0_0_20px_rgba(99,102,241,0.05)]
        hover:border-zinc-700 hover:shadow-[0_4px_35px_rgba(0,0,0,0.5)]
        transition-all duration-300 ease-out cursor-pointer select-none group
      `}
    >
      {/* Pulse Status Ring */}
      <div className="flex items-center gap-2">
        <span className="relative flex h-2 w-2">
          {isAnalyzing ? (
            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-indigo-400 opacity-75" />
          ) : (
            <span className={`animate-pulse absolute inline-flex h-full w-full rounded-full opacity-75 ${score >= 70 ? 'bg-emerald-400' : 'bg-red-400'}`} />
          )}
          <span className={`relative inline-flex rounded-full h-2 w-2 ${isAnalyzing ? 'bg-indigo-500' : score >= 70 ? 'bg-emerald-500' : 'bg-red-500'}`} />
        </span>
        <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest font-semibold">
          AI DBA
        </span>
      </div>

      <div className="w-px h-4 bg-zinc-800" />

      {/* Health Score Badge */}
      <div className={`flex items-center gap-2 px-3 py-1 rounded-xl border ${scoreColorClass} transition-all duration-300 font-mono`}>
        {isAnalyzing ? (
          <Loader2 className="w-4 h-4 animate-spin text-indigo-400" />
        ) : (
          <Database className="w-4 h-4" />
        )}
        <span className="text-sm font-bold tracking-tight">
          {isAnalyzing ? '...' : `${score}/100`}
        </span>
      </div>

      <div className="w-px h-4 bg-zinc-800" />

      {/* Issues Summary Badge */}
      <div className="flex items-center gap-3.5 text-xs font-medium">
        {/* Errors */}
        <div className="flex items-center gap-1.5 text-zinc-400 group-hover:text-zinc-200 transition-colors">
          <AlertCircle className={`w-4 h-4 ${errors > 0 ? 'text-red-500 animate-pulse' : 'text-zinc-600'}`} />
          <span className={errors > 0 ? 'text-red-400 font-bold' : ''}>{errors} Error{errors !== 1 ? 's' : ''}</span>
        </div>

        {/* Warnings */}
        <div className="flex items-center gap-1.5 text-zinc-400 group-hover:text-zinc-200 transition-colors">
          <AlertTriangle className={`w-4 h-4 ${warnings > 0 ? 'text-amber-500' : 'text-zinc-600'}`} />
          <span className={warnings > 0 ? 'text-amber-400 font-bold' : ''}>{warnings} Warning{warnings !== 1 ? 's' : ''}</span>
        </div>

        {/* Infos */}
        <div className="flex items-center gap-1.5 text-zinc-400 group-hover:text-zinc-200 transition-colors">
          <HelpCircle className="w-4 h-4 text-sky-500" />
          <span>{infos} Suggestion{infos !== 1 ? 's' : ''}</span>
        </div>
      </div>
    </div>
  );
}
