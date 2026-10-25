import React from 'react';
import { useQuotaStore } from '../../../../store/useQuotaStore';
import { cardClass } from './ui';
import type { LocalStats } from './useLocalStats';

export default function AnalyticsTab({ stats }: { stats: LocalStats }) {
  const { dailyLimit, used, remaining } = useQuotaStore();

  return (
    <div className="space-y-6 animate-in fade-in duration-200">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider">Schemas Compiled</span>
          <p className="text-2xl font-bold text-content-primary">{stats.schemas}</p>
          <div className="text-[11px] text-content-muted uppercase tracking-wide">Local WASM Compiler</div>
        </div>
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider">AI Credits Used</span>
          <p className="text-2xl font-bold text-content-primary">{Math.round((used / dailyLimit) * 100)}%</p>
          <div className="text-[11px] text-content-muted uppercase tracking-wide">{Math.round((remaining / dailyLimit) * 100)}% Remaining Today</div>
        </div>
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider">DBA Audits Run</span>
          <p className="text-2xl font-bold text-content-primary">{stats.dbaAudits}</p>
          <div className="text-[11px] text-content-muted uppercase tracking-wide">Linter Session</div>
        </div>
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider">Mock Rows Seeded</span>
          <p className="text-2xl font-bold text-content-primary">{stats.mockRecords}</p>
          <div className="text-[11px] text-content-muted uppercase tracking-wide">Smart Seeding Engine</div>
        </div>
      </div>

      <div className="space-y-6 flex flex-col w-full">
        {/* AI Engine Routing Allocation */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <div className="flex justify-between items-center text-[11px] font-medium text-content-muted uppercase tracking-wider">
            <span>AI Engine Allocation Share</span>
            <span className="text-content-muted">COMPILATION BREAKDOWN</span>
          </div>
          <div className="w-full h-4 rounded-full overflow-hidden flex bg-surface-900 border border-line">
            <div className="h-full bg-accent/40" style={{ width: '55%' }} title="Local Engine: 55%" />
            <div className="h-full bg-accent/60" style={{ width: '25%' }} title="NAI v1: 25%" />
            <div className="h-full bg-accent" style={{ width: '15%' }} title="NAI v1 Pro: 15%" />
            <div className="h-full bg-accent-text" style={{ width: '5%' }} title="Custom: 5%" />
          </div>
          <div className="flex flex-wrap gap-x-6 gap-y-2 text-[11px] font-medium text-content-muted uppercase tracking-wide">
            <div className="flex items-center gap-2"><div className="w-2.5 h-2.5 rounded-full bg-accent/40" /><span>Local (55%)</span></div>
            <div className="flex items-center gap-2"><div className="w-2.5 h-2.5 rounded-full bg-accent/60" /><span>NAI v1 (25%)</span></div>
            <div className="flex items-center gap-2"><div className="w-2.5 h-2.5 rounded-full bg-accent" /><span>NAI v1 Pro (15%)</span></div>
            <div className="flex items-center gap-2"><div className="w-2.5 h-2.5 rounded-full bg-accent-text" /><span>Custom (5%)</span></div>
          </div>
        </div>

        {/* Database Dialect Target Distribution */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider block">Target Database Compilations</span>
          <div className="space-y-3">
            {[
              ['PostgreSQL', 45], ['Microsoft SQL Server', 25], ['MySQL / MariaDB', 18], ['SQLite (Embedded)', 12],
            ].map(([label, pct]) => (
              <div key={label as string} className="space-y-1.5">
                <div className="flex justify-between text-xs font-medium text-content-secondary">
                  <span>{label}</span>
                  <span className="text-content-muted font-mono">{pct}%</span>
                </div>
                <div className="w-full h-2 bg-surface-900 rounded-full overflow-hidden border border-line">
                  <div className="h-full bg-accent-text rounded-full" style={{ width: `${pct}%` }} />
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Weekly Performance Metrics */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider block">Weekly Performance Metrics</span>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="p-3.5 bg-surface-900/60 border border-line rounded-[var(--radius-control)] space-y-2">
              <div className="flex justify-between text-[11px] font-medium uppercase text-content-secondary">
                <span>WASM Compiler Success Rate</span>
                <span className="text-content-primary font-semibold">100%</span>
              </div>
              <div className="w-full h-2 bg-surface-900 rounded-full overflow-hidden border border-line">
                <div className="h-full bg-accent-text rounded-full" style={{ width: '100%' }} />
              </div>
            </div>
            <div className="p-3.5 bg-surface-900/60 border border-line rounded-[var(--radius-control)] space-y-2">
              <div className="flex justify-between text-[11px] font-medium uppercase text-content-secondary">
                <span>DBA Integrity Rating</span>
                <span className="text-content-secondary font-semibold">94.8%</span>
              </div>
              <div className="w-full h-2 bg-surface-900 rounded-full overflow-hidden border border-line">
                <div className="h-full bg-accent-text rounded-full" style={{ width: '94.8%' }} />
              </div>
            </div>
          </div>
        </div>

        {/* Operations History Log */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <div className="border-b border-line pb-3">
            <h4 className="text-xs font-semibold text-content-primary uppercase tracking-wider">Recent Operations Audit Log</h4>
          </div>
          <div className="space-y-2 max-h-[200px] overflow-y-auto pr-1">
            {stats.aiRequests === 0 && stats.dbaAudits === 0 && stats.schemas === 0 ? (
              <div className="flex flex-col items-center justify-center py-8 text-center gap-1.5">
                <span className="text-content-muted text-xs font-medium">No activity yet</span>
                <span className="text-content-muted text-[11px]">Your operations will appear here as you use Namines.</span>
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between text-xs p-3 bg-surface-900/60 border border-line hover:bg-surface-700/60 rounded-[var(--radius-control)] transition-colors">
                  <span className="font-medium text-content-secondary">Local WASM compiler schema generation</span>
                  <span className="text-[10px] font-mono border border-line-strong bg-surface-700 text-content-primary px-2 py-0.5 rounded-[var(--radius-control)] font-semibold">SUCCESS</span>
                </div>
                <div className="flex items-center justify-between text-xs p-3 bg-surface-900/60 border border-line hover:bg-surface-700/60 rounded-[var(--radius-control)] transition-colors">
                  <span className="font-medium text-content-secondary">{Math.round((used / dailyLimit) * 100)}% daily credits checked</span>
                  <span className="text-[10px] font-mono border border-line bg-surface-700 text-content-secondary px-2 py-0.5 rounded-[var(--radius-control)] font-semibold">INFO</span>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
