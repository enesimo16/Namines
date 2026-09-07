'use client';

import { useState } from 'react';
import Link from 'next/link';
import {
  Check,
  CheckCircle2,
  Copy,
  Database,
  ExternalLink,
  Layers,
  Loader2,
  Plus,
  RefreshCw,
  Terminal,
  Zap,
} from 'lucide-react';

interface BlueprintOption {
  id: string;
  name: string;
  tables: number;
  fks: number;
}

const BLUEPRINT_OPTIONS: BlueprintOption[] = [
  { id: 'ecommerce', name: 'E-Commerce Store', tables: 14, fks: 22 },
  { id: 'saas', name: 'SaaS Multi-tenant', tables: 16, fks: 25 },
  { id: 'fintech', name: 'Fintech & Ledger', tables: 18, fks: 28 },
  { id: 'ai', name: 'AI Agent Knowledge', tables: 8, fks: 12 },
  { id: 'healthcare', name: 'Healthcare (HIPAA)', tables: 20, fks: 32 },
  { id: 'workflow', name: 'Workflow Engine', tables: 10, fks: 15 },
];

const ENGINES = ['PostgreSQL 17', 'MySQL 8.4', 'SQLite 3', 'SQL Server 2022'] as const;

export default function ClickClickDone() {
  const [selectedBlueprint, setSelectedBlueprint] = useState<BlueprintOption>(BLUEPRINT_OPTIONS[0]!);
  const [selectedEngineIndex, setSelectedEngineIndex] = useState(0);
  const [isVerifying, setIsVerifying] = useState(false);
  const [lastVerifiedTime, setLastVerifiedTime] = useState('0.04s');
  const [copiedPush, setCopiedPush] = useState(false);
  const [activeStep3Tab, setActiveStep3Tab] = useState<number | null>(null);

  const handleRunVerification = () => {
    setIsVerifying(true);
    setTimeout(() => {
      setIsVerifying(false);
      setLastVerifiedTime((0.03 + Math.random() * 0.04).toFixed(2) + 's');
    }, 450);
  };

  const handleCycleEngine = () => {
    setSelectedEngineIndex((prev) => (prev + 1) % ENGINES.length);
  };

  const handleCopyPush = () => {
    navigator.clipboard.writeText('npx namines push');
    setCopiedPush(true);
    setTimeout(() => setCopiedPush(false), 2000);
  };

  const currentEngine = ENGINES[selectedEngineIndex];

  return (
    <section id="how-it-works" className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-20 sm:py-28">
      {/* Section Header (Distinct, rhythmic Namines heading) */}
      <div className="mb-14 sm:mb-16">
        <h2 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight text-content-primary">
          Design, verify, deploy.
        </h2>
        <p className="mt-3 text-sm sm:text-base text-content-muted max-w-2xl leading-relaxed">
          From an interactive blueprint or prompt to proven zero-drift database lifecycle in three seamless steps.
        </p>
      </div>

      {/* 3 Columns Grid — All Cards With Strictly Equal Heights */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8 lg:gap-6 items-stretch">
        {/* Step 1: Select or Prompt a Blueprint */}
        <div className="flex flex-col h-full justify-between">
          <div>
            <div className="flex items-center gap-3 mb-2.5">
              <div
                className="w-7 h-7 rounded-[var(--radius-control)] flex items-center justify-center font-mono font-bold text-xs shadow-md"
                style={{ backgroundColor: 'var(--namines-teal-badge)', color: 'var(--surface-900)' }}
              >
                1
              </div>
              <h3 className="text-lg font-bold text-content-primary">
                Select a blueprint
              </h3>
            </div>
            <p className="text-xs text-content-muted leading-relaxed min-h-[36px] mb-5">
              Pick a production schema template or describe your architecture in plain English.
            </p>
          </div>

          {/* Equal Height Card 1 */}
          <div className="h-[390px] rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-3.5 flex flex-col justify-between shadow-xl">
            {/* Card Header Action */}
            <Link
              href="/new"
              className="flex items-center justify-between px-3 py-2 text-xs font-mono text-content-muted hover:text-content-primary hover:bg-surface-800 rounded-[var(--radius-control)] border border-dashed border-surface-600 transition-colors"
            >
              <div className="flex items-center gap-2">
                <Plus className="w-3.5 h-3.5 text-accent" />
                <span className="font-semibold">Describe your own with AI</span>
              </div>
              <ExternalLink className="w-3 h-3 text-content-subtle" />
            </Link>

            {/* Clickable Blueprints List */}
            <div className="space-y-1.5 my-auto">
              {BLUEPRINT_OPTIONS.map((b) => {
                const isSelected = selectedBlueprint.id === b.id;
                return (
                  <button
                    key={b.id}
                    type="button"
                    onClick={() => setSelectedBlueprint(b)}
                    className={`w-full flex items-center justify-between px-3 py-2 rounded-[var(--radius-control)] text-xs font-mono transition-all cursor-pointer ${
                      isSelected
                        ? 'bg-accent text-surface-900 font-bold shadow-md'
                        : 'text-content-secondary hover:bg-surface-800 hover:text-content-primary'
                    }`}
                  >
                    <div className="flex items-center gap-2.5">
                      <Database className={`w-3.5 h-3.5 ${isSelected ? 'text-surface-900' : 'text-content-muted'}`} />
                      <span>{b.name}</span>
                    </div>
                    <div className="flex items-center gap-2">
                      <span className={`text-[10px] ${isSelected ? 'text-surface-900/80 font-semibold' : 'text-content-subtle'}`}>
                        {b.tables} tables
                      </span>
                      {isSelected && <Check className="w-3.5 h-3.5 text-surface-900 stroke-[2.5]" />}
                    </div>
                  </button>
                );
              })}
            </div>

            {/* Footer Status */}
            <div className="pt-2 border-t border-surface-600/60 flex items-center justify-between text-[11px] font-mono text-content-subtle">
              <span>Active: {selectedBlueprint.name}</span>
              <span className="text-accent-text">{selectedBlueprint.fks} relations</span>
            </div>
          </div>
        </div>

        {/* Step 2: Verify Architecture with Deterministic Rules */}
        <div className="flex flex-col h-full justify-between">
          <div>
            <div className="flex items-center gap-3 mb-2.5">
              <div
                className="w-7 h-7 rounded-[var(--radius-control)] flex items-center justify-center font-mono font-bold text-xs shadow-md"
                style={{ backgroundColor: 'var(--namines-teal-badge)', color: 'var(--surface-900)' }}
              >
                2
              </div>
              <h3 className="text-lg font-bold text-content-primary">
                Verify your architecture
              </h3>
            </div>
            <p className="text-xs text-content-muted leading-relaxed min-h-[36px] mb-5">
              24 static AST rules verify foreign keys, indexing, and non-destructive transitions.
            </p>
          </div>

          {/* Equal Height Card 2 */}
          <div className="h-[390px] rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-3.5 flex flex-col justify-between shadow-xl">
            <div className="space-y-2">
              {/* Interactive Target Engine Row */}
              <button
                type="button"
                onClick={handleCycleEngine}
                title="Click to cycle target engine"
                className="w-full flex items-center justify-between text-xs font-mono p-2.5 bg-surface-800/80 hover:bg-surface-700/80 rounded-[var(--radius-control)] border border-surface-500/60 transition-colors cursor-pointer group"
              >
                <span className="text-content-muted group-hover:text-content-primary">Target Engine</span>
                <div className="flex items-center gap-1.5 font-bold text-accent-text">
                  <span>{currentEngine}</span>
                  <RefreshCw className="w-3 h-3 opacity-60 group-hover:opacity-100 group-hover:rotate-180 transition-transform" />
                </div>
              </button>

              {/* Dialect Rules Row */}
              <div className="flex items-center justify-between text-xs font-mono p-2.5 bg-surface-800/80 rounded-[var(--radius-control)] border border-surface-500/60">
                <span className="text-content-muted">Dialect Rules</span>
                <span className="text-content-primary font-semibold">Strict 3NF + FK Check</span>
              </div>

              {/* Rule Engine Safety Tier */}
              <div className="flex items-center justify-between text-xs font-mono p-2.5 bg-surface-800/80 rounded-[var(--radius-control)] border border-surface-500/60">
                <span className="text-content-muted">Rule Engine</span>
                <span className="text-success font-semibold flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3" />
                  Production Safe
                </span>
              </div>

              {/* Clickable Run Verification Button */}
              <button
                type="button"
                onClick={handleRunVerification}
                disabled={isVerifying}
                className="w-full py-2.5 px-3 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 active:scale-98 border border-surface-400 text-xs font-mono font-bold text-content-primary flex items-center justify-center gap-2 shadow-sm transition-all cursor-pointer"
              >
                {isVerifying ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 text-accent animate-spin" />
                    <span>Running Static Analysis...</span>
                  </>
                ) : (
                  <>
                    <Zap className="w-3.5 h-3.5 text-accent-text" />
                    <span>Run Verification</span>
                  </>
                )}
              </button>
            </div>

            {/* Terminal Diagnostic Log Output */}
            <div className="p-3 bg-surface-800 rounded-[var(--radius-control)] border border-surface-500/80 font-mono text-[11px] flex flex-col gap-1">
              <div className="flex items-center justify-between text-success">
                <div className="flex items-center gap-1.5">
                  <CheckCircle2 className="w-3.5 h-3.5 text-success" />
                  <span className="font-bold">Verified in {lastVerifiedTime}</span>
                </div>
                <span className="text-[10px] text-content-subtle">24 AST rules</span>
              </div>
              <div className="text-content-muted text-[10px]">
                &rarr; Checked {selectedBlueprint.tables} tables &amp; {selectedBlueprint.fks} foreign keys
              </div>
              <div className="text-content-secondary font-medium text-[10px]">
                &rarr; Schema certified production ready
              </div>
            </div>
          </div>
        </div>

        {/* Step 3: Deploy with Zero Drift */}
        <div className="flex flex-col h-full justify-between">
          <div>
            <div className="flex items-center gap-3 mb-2.5">
              <div
                className="w-7 h-7 rounded-[var(--radius-control)] flex items-center justify-center font-mono font-bold text-xs shadow-md"
                style={{ backgroundColor: 'var(--namines-teal-badge)', color: 'var(--surface-900)' }}
              >
                3
              </div>
              <h3 className="text-lg font-bold text-content-primary">
                Namines does the rest
              </h3>
            </div>
            <p className="text-xs text-content-muted leading-relaxed min-h-[36px] mb-5">
              Get genuine multi-engine DDL, zero-downtime migrations, and an instant CRUD console.
            </p>
          </div>

          {/* Equal Height Card 3 */}
          <div className="h-[390px] rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-3.5 flex flex-col justify-between shadow-xl">
            {/* Clickable Terminal Push Header */}
            <button
              type="button"
              onClick={handleCopyPush}
              title="Click to copy push command"
              className="w-full p-2.5 bg-surface-800 hover:bg-surface-700 rounded-[var(--radius-control)] border border-surface-500/80 font-mono text-xs text-content-primary flex items-center justify-between transition-colors cursor-pointer group"
            >
              <div className="flex items-center gap-2">
                <Terminal className="w-3.5 h-3.5 text-accent-text" />
                <span className="text-content-subtle">~/schema $</span>
                <span className="font-bold text-content-primary">namines push</span>
              </div>
              <div className="flex items-center gap-1 text-[11px] text-content-muted group-hover:text-accent-text">
                {copiedPush ? (
                  <>
                    <Check className="w-3 h-3 text-success" />
                    <span>Copied</span>
                  </>
                ) : (
                  <>
                    <Copy className="w-3 h-3" />
                    <span>Copy</span>
                  </>
                )}
              </div>
            </button>

            {/* Clickable Status Cards List */}
            <div className="space-y-2">
              <button
                type="button"
                onClick={() => setActiveStep3Tab(activeStep3Tab === 0 ? null : 0)}
                className={`w-full text-left flex items-start gap-2.5 p-2.5 rounded-[var(--radius-control)] border transition-all cursor-pointer ${
                  activeStep3Tab === 0
                    ? 'bg-surface-700/80 border-accent/60'
                    : 'bg-surface-800/60 hover:bg-surface-800 border-surface-500/40'
                }`}
              >
                <CheckCircle2 className="w-4 h-4 text-success shrink-0 mt-0.5" />
                <div className="flex flex-col min-w-0">
                  <span className="text-xs font-mono font-semibold text-content-primary">
                    Automatic check: 3NF Passed
                  </span>
                  <span className="text-[10px] font-mono text-content-subtle">
                    0 warnings &middot; 0 orphaned FKs
                  </span>
                </div>
              </button>

              <button
                type="button"
                onClick={() => setActiveStep3Tab(activeStep3Tab === 1 ? null : 1)}
                className={`w-full text-left flex items-start gap-2.5 p-2.5 rounded-[var(--radius-control)] border transition-all cursor-pointer ${
                  activeStep3Tab === 1
                    ? 'bg-surface-700/80 border-accent/60'
                    : 'bg-surface-800/60 hover:bg-surface-800 border-surface-500/40'
                }`}
              >
                <CheckCircle2 className="w-4 h-4 text-success shrink-0 mt-0.5" />
                <div className="flex flex-col min-w-0">
                  <span className="text-xs font-mono font-semibold text-content-primary">
                    Migration generated: Zero drops
                  </span>
                  <span className="text-[10px] font-mono text-content-subtle">
                    CONCURRENTLY &middot; lock-timeout guarded
                  </span>
                </div>
              </button>

              <button
                type="button"
                onClick={() => setActiveStep3Tab(activeStep3Tab === 2 ? null : 2)}
                className={`w-full text-left flex items-start gap-2.5 p-2.5 rounded-[var(--radius-control)] border transition-all cursor-pointer ${
                  activeStep3Tab === 2
                    ? 'bg-surface-700/80 border-accent/60'
                    : 'bg-surface-800/60 hover:bg-surface-800 border-surface-500/40'
                }`}
              >
                <CheckCircle2 className="w-4 h-4 text-success shrink-0 mt-0.5" />
                <div className="flex flex-col min-w-0">
                  <span className="text-xs font-mono font-semibold text-content-primary">
                    Namines Desk: Live CRUD admin
                  </span>
                  <span className="text-[10px] font-mono text-content-subtle">
                    Auto-generated admin UI &middot; read-only console
                  </span>
                </div>
              </button>
            </div>

            {/* Step 3 Link to Demo */}
            <div className="pt-2 border-t border-surface-600/60 flex items-center justify-between text-[11px] font-mono text-content-subtle">
              <span>Ready for {currentEngine}</span>
              <Link href="/demo" className="text-accent-text hover:underline flex items-center gap-1 font-bold">
                <Layers className="w-3 h-3" />
                <span>Open Blueprints</span>
              </Link>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
