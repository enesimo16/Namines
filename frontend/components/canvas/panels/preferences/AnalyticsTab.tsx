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
          <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">Schemas Compiled</span>
          <p className="text-2xl font-bold text-content-primary">{stats.schemas}</p>
          <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">Local WASM Compiler</div>
        </div>
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">AI Credits Used</span>
          <p className="text-2xl font-bold text-content-primary">{Math.round((used / dailyLimit) * 100)}%</p>
          <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">{Math.round((remaining / dailyLimit) * 100)}% Remaining Today</div>
        </div>
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">DBA Audits Run</span>
          <p className="text-2xl font-bold text-content-primary">{stats.dbaAudits}</p>
          <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">Linter Session</div>
        </div>
        <div className={`${cardClass} p-4 space-y-1`}>
          <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">Mock Rows Seeded</span>
          <p className="text-2xl font-bold text-content-primary">{stats.mockRecords}</p>
          <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">Smart Seeding Engine</div>
        </div>
      </div>

      <div className="space-y-6 flex flex-col w-full">
        {/* AI Engine Routing Allocation */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <div className="flex justify-between items-center text-[10px] font-semibold text-content-muted uppercase tracking-wider">
            <span>AI Engine Allocation Share</span>
            <span className="text-content-subtle">COMPILATION BREAKDOWN</span>
          </div>
          <div className="w-full h-5 rounded-[var(--radius-control)] overflow-hidden flex bg-surface-600">
            <div className="h-full bg-white/[0.15]" style={{ width: '55%' }} title="Local Engine: 55%" />
            <div className="h-full bg-white/[0.25]" style={{ width: '25%' }} title="NAI v1: 25%" />
            <div className="h-full bg-white/[0.4]" style={{ width: '15%' }} title="NAI v1 Pro: 15%" />
            <div className="h-full bg-white/[0.6]" style={{ width: '5%' }} title="Custom: 5%" />
          </div>
          <div className="flex flex-wrap gap-x-6 gap-y-2 text-[10px] font-semibold text-content-muted uppercase tracking-wide">
            <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.15]" /><span>Local (55%)</span></div>
            <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.25]" /><span>NAI v1 (25%)</span></div>
            <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.4]" /><span>NAI v1 Pro (15%)</span></div>
            <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.6]" /><span>Custom (5%)</span></div>
          </div>
        </div>

        {/* Database Dialect Target Distribution */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <span className="text-[10px] font-semibold text-content-muted uppercase tracking-wider block">Target Database Compilations</span>
          <div className="space-y-3">
            {[
              ['PostgreSQL', 45], ['Microsoft SQL Server', 25], ['MySQL / MariaDB', 18], ['SQLite (Embedded)', 12],
            ].map(([label, pct]) => (
              <div key={label as string} className="space-y-1.5">
                <div className="flex justify-between text-xs font-semibold text-content-secondary">
                  <span>{label}</span>
                  <span className="text-content-muted">{pct}%</span>
                </div>
                <div className="w-full h-2 bg-surface-600 rounded-full overflow-hidden">
                  <div className="h-full bg-content-secondary rounded-full" style={{ width: `${pct}%` }} />
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Weekly Performance Metrics */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <span className="text-[10px] font-semibold text-content-muted uppercase tracking-wider block">Weekly Performance Metrics</span>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="p-3.5 bg-surface-600 rounded-[var(--radius-control)] space-y-2">
              <div className="flex justify-between text-[11px] font-semibold uppercase text-content-secondary">
                <span>WASM Compiler Success Rate</span>
                <span className="text-success-text font-bold">100%</span>
              </div>
              <div className="w-full h-2 bg-surface-800 rounded-full overflow-hidden">
                <div className="h-full bg-success-text rounded-full" style={{ width: '100%' }} />
              </div>
            </div>
            <div className="p-3.5 bg-surface-600 rounded-[var(--radius-control)] space-y-2">
              <div className="flex justify-between text-[11px] font-semibold uppercase text-content-secondary">
                <span>DBA Integrity Rating</span>
                <span className="text-content-secondary font-bold">94.8%</span>
              </div>
              <div className="w-full h-2 bg-surface-800 rounded-full overflow-hidden">
                <div className="h-full bg-content-secondary rounded-full" style={{ width: '94.8%' }} />
              </div>
            </div>
          </div>
        </div>

        {/* Operations History Log */}
        <div className={`${cardClass} p-5 space-y-4 w-full`}>
          <div className="border-b border-content-primary/10 pb-3">
            <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">Recent Operations Audit Log</h4>
          </div>
          <div className="space-y-2.5 max-h-[200px] overflow-y-auto pr-1">
            {stats.aiRequests === 0 && stats.dbaAudits === 0 && stats.schemas === 0 ? (
              <div className="flex flex-col items-center justify-center py-8 text-center gap-1.5">
                <span className="text-content-muted text-xs font-semibold">No activity yet</span>
                <span className="text-content-subtle text-[10px]">Your operations will appear here as you use Namines.</span>
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between text-xs p-3 bg-surface-600 hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-all">
                  <span className="font-semibold text-content-secondary">Local WASM compiler schema generation</span>
                  <span className="text-micro bg-success-text/10 text-success-text px-2.5 py-0.5 rounded-[var(--radius-control)] font-bold">SUCCESS</span>
                </div>
                <div className="flex items-center justify-between text-xs p-3 bg-surface-600 hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-all">
                  <span className="font-semibold text-content-secondary">{Math.round((used / dailyLimit) * 100)}% daily credits checked</span>
                  <span className="text-micro bg-white/[0.08] text-content-secondary px-2.5 py-0.5 rounded-[var(--radius-control)] font-bold">INFO</span>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
