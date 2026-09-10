'use client';

import { CheckCircle2, Terminal } from 'lucide-react';

interface MetricItem {
  label: string;
  value: string;
}

interface ServiceCardProps {
  name: string;
  badge: string;
  metrics: MetricItem[];
  sparklineD: string;
}

function ServiceCard({ name, badge, metrics, sparklineD }: ServiceCardProps) {
  return (
    <div className="relative rounded-[var(--radius-card)] bg-surface-800/30 border border-surface-500/30 hover:border-surface-400/50 hover:bg-surface-800/50 transition-all p-3.5 flex flex-col justify-between group">
      {/* Card Header */}
      <div className="flex items-center justify-between mb-2">
        <span className="text-xs font-mono font-bold text-content-primary flex items-center gap-1.5">
          <span className="w-1.5 h-1.5 rounded-full bg-success" />
          {name}
        </span>
        <span className="text-[11px] font-mono text-success-text flex items-center gap-1">
          <CheckCircle2 className="w-3 h-3" />
          {badge}
        </span>
      </div>

      {/* Sparkline Graph */}
      <div className="h-9 w-full my-1 relative overflow-hidden">
        <svg className="w-full h-full" viewBox="0 0 100 28" preserveAspectRatio="none">
          <defs>
            <linearGradient id={`grad-${name}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--namines-teal-vibrant)" stopOpacity="0.25" />
              <stop offset="100%" stopColor="var(--namines-teal-vibrant)" stopOpacity="0.0" />
            </linearGradient>
          </defs>
          <path d={`${sparklineD} L 100 28 L 0 28 Z`} fill={`url(#grad-${name})`} />
          <path
            d={sparklineD}
            fill="none"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="1.6"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </div>

      {/* 2x2 Metrics Grid */}
      <div className="grid grid-cols-2 gap-x-2 gap-y-1 pt-2 border-t border-surface-500/30">
        {metrics.map((m, i) => (
          <div key={i} className="flex flex-col">
            <span className="text-[10px] font-mono uppercase tracking-wider text-content-subtle">
              {m.label}
            </span>
            <span className="text-[11px] font-mono font-semibold text-content-secondary group-hover:text-content-primary transition-colors">
              {m.value}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function ProductionConsoleMockup() {
  return (
    <div className="relative w-full">
      {/* Ambient background soft glow behind console */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -inset-6 rounded-full opacity-20 blur-3xl"
        style={{
          background: 'radial-gradient(circle, var(--namines-teal-glow) 0%, transparent 70%)',
        }}
      />

      {/* Floating Monospace Terminal Pill */}
      <div className="absolute -top-4 left-6 z-20 flex items-center gap-2 px-3.5 py-1.5 bg-surface-800/90 border border-surface-500/60 rounded-[var(--radius-control)] shadow-xl text-xs font-mono text-content-primary backdrop-blur-md">
        <Terminal className="w-3.5 h-3.5 text-accent-text" />
        <span className="text-content-subtle">$</span>
        <span className="text-content-primary font-bold">namines push</span>
      </div>

      {/* Main Console Window (Soft, border-free / sleek glass aesthetic) */}
      <div className="relative rounded-[var(--radius-modal)] border border-surface-500/30 bg-surface-900/50 p-4 sm:p-5 pt-8 backdrop-blur-xl shadow-2xl overflow-hidden">
        {/* Header Strip */}
        <div className="flex items-center justify-between pb-3 mb-3.5 border-b border-surface-500/30">
          <div className="flex items-center gap-2">
            <span className="text-[10px] font-mono tracking-widest font-bold uppercase text-content-subtle">
              PRODUCTION ARCHITECTURE
            </span>
          </div>
          <div className="flex items-center gap-2">
            <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800/60 border border-surface-500/40 text-[11px] font-mono text-content-secondary">
              PostgreSQL 17
            </span>
            <div className="w-2 h-2 rounded-full bg-success animate-pulse" />
          </div>
        </div>

        {/* 2x2 Services Grid */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <ServiceCard
            name="app-backend"
            badge="Available"
            metrics={[
              { label: 'Memory', value: '142MB' },
              { label: 'CPU', value: '0.8%' },
              { label: 'Instances', value: '2' },
              { label: 'Requests', value: '2.1k/s' },
            ]}
            sparklineD="M 0 20 Q 25 6 50 14 T 100 8"
          />

          <ServiceCard
            name="app-database"
            badge="Available"
            metrics={[
              { label: 'Connections', value: '18 Active' },
              { label: 'Storage', value: '1.2GB' },
              { label: 'IOPS', value: '420' },
              { label: 'Replication', value: 'Sync' },
            ]}
            sparklineD="M 0 16 Q 30 24 60 10 T 100 6"
          />

          <ServiceCard
            name="app-frontend"
            badge="Available"
            metrics={[
              { label: 'Bandwidth', value: '4.8GB' },
              { label: 'Latency', value: '18ms' },
              { label: 'Status', value: '200 OK' },
              { label: 'SSL', value: 'TLS 1.3' },
            ]}
            sparklineD="M 0 22 Q 35 10 70 18 T 100 8"
          />

          <ServiceCard
            name="migration-diff"
            badge="Available"
            metrics={[
              { label: 'Safety', value: '100% Safe' },
              { label: 'Drift', value: '0% None' },
              { label: 'Validation', value: 'Strict 3NF' },
              { label: 'Locks', value: 'Zero' },
            ]}
            sparklineD="M 0 14 Q 20 6 55 16 T 100 10"
          />
        </div>

        {/* Bottom Status Bar */}
        <div className="mt-3.5 pt-2.5 border-t border-surface-500/25 flex items-center justify-between text-[11px] font-mono text-content-subtle">
          <span className="flex items-center gap-1.5">
            <span className="w-1.5 h-1.5 rounded-full bg-success" />
            Live architecture healthy
          </span>
          <span className="text-content-subtle">v2.1 live</span>
        </div>
      </div>
    </div>
  );
}
