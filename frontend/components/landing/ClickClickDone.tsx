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
  dialectRules: string;
  safetyTier: string;
  migrationFile: string;
  checks: [
    { title: string; subtitle: string; codePreview: string },
    { title: string; subtitle: string; codePreview: string },
    { title: string; subtitle: string; codePreview: string },
  ];
}

const BLUEPRINT_OPTIONS: BlueprintOption[] = [
  {
    id: 'ecommerce',
    name: 'E-Commerce Store',
    tables: 14,
    fks: 22,
    dialectRules: 'Strict 3NF + Stock Lock Check',
    safetyTier: 'Production Safe',
    migrationFile: '20261018_create_orders_inventory.sql',
    checks: [
      {
        title: 'Automatic check: 3NF Passed',
        subtitle: '0 warnings · 0 orphaned FKs',
        codePreview: 'CHECK (quantity > 0) AND expires_at > created_at',
      },
      {
        title: 'Migration: Zero Lock DDL',
        subtitle: 'CONCURRENTLY · lock-timeout guarded',
        codePreview: 'CREATE INDEX CONCURRENTLY idx_orders_user ON orders(user_id);',
      },
      {
        title: 'Namines Desk: Orders Admin',
        subtitle: 'Auto-generated CRUD · read-only console',
        codePreview: 'Desk.registerResource("orders", { table: "orders", permissions: ["read", "write"] });',
      },
    ],
  },
  {
    id: 'saas',
    name: 'SaaS Multi-tenant',
    tables: 16,
    fks: 25,
    dialectRules: 'Tenant Isolation + RLS Check',
    safetyTier: 'Multi-Tenant Hardened',
    migrationFile: '20261018_saas_org_tenants.sql',
    checks: [
      {
        title: 'Automatic check: RLS Policies Injected',
        subtitle: 'Row-Level Security on all 16 tables',
        codePreview: 'CREATE POLICY org_isolation ON workspaces FOR ALL USING (org_id = current_setting(\'app.current_org_id\'));',
      },
      {
        title: 'Migration: Safe Cascade Paths',
        subtitle: '0 circular CASCADE dependencies',
        codePreview: 'ALTER TABLE members ADD CONSTRAINT fk_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE RESTRICT;',
      },
      {
        title: 'Namines Desk: Tenant Console',
        subtitle: 'Instant multi-org switcher & RBAC UI',
        codePreview: 'Desk.enableTenantSwitching({ headerField: "X-Org-Id", roleField: "role" });',
      },
    ],
  },
  {
    id: 'fintech',
    name: 'Fintech & Ledger',
    tables: 18,
    fks: 28,
    dialectRules: 'Immutable Ledger + Balance Check',
    safetyTier: 'ACID Strict Enforced',
    migrationFile: '20261018_ledger_double_entry.sql',
    checks: [
      {
        title: 'Automatic check: Zero Negative Balance',
        subtitle: 'Deterministic CHECK constraint verified',
        codePreview: 'CONSTRAINT chk_ledger_balance CHECK (balance >= 0 AND currency IN (\'USD\', \'EUR\', \'TRY\'))',
      },
      {
        title: 'Migration: Non-destructive Triggers',
        subtitle: 'Append-only audit trail compiled',
        codePreview: 'CREATE TRIGGER audit_transaction BEFORE UPDATE OR DELETE ON transactions EXECUTE FUNCTION raise_immutable();',
      },
      {
        title: 'Namines Desk: Audit Ledger',
        subtitle: 'Immutable transaction explorer & log viewer',
        codePreview: 'Desk.registerResource("transactions", { table: "transactions", readOnly: true });',
      },
    ],
  },
  {
    id: 'ai',
    name: 'AI Agent Knowledge',
    tables: 8,
    fks: 12,
    dialectRules: 'Vector Cosine HNSW + Chunk Guard',
    safetyTier: 'pgvector Certified',
    migrationFile: '20261018_agent_embeddings.sql',
    checks: [
      {
        title: 'Automatic check: Vector Index Ops',
        subtitle: 'Cosine distance ops verified',
        codePreview: 'CREATE INDEX idx_embeddings_hnsw ON agent_memories USING hnsw (embedding vector_cosine_ops);',
      },
      {
        title: 'Migration: JSONB Metadata Schema',
        subtitle: 'Structured metadata validation guard',
        codePreview: 'ALTER TABLE agent_memories ADD CONSTRAINT chk_metadata_json CHECK (jsonb_typeof(metadata) = \'object\');',
      },
      {
        title: 'Namines Desk: Vector Explorer',
        subtitle: 'Embedding visualizer & prompt testing UI',
        codePreview: 'Desk.registerResource("agent_memories", { table: "agent_memories", vectorField: "embedding" });',
      },
    ],
  },
  {
    id: 'healthcare',
    name: 'Healthcare (HIPAA)',
    tables: 20,
    fks: 32,
    dialectRules: 'HIPAA Audit Columns + AES-256',
    safetyTier: 'HIPAA Compliant',
    migrationFile: '20261018_hipaa_patient_records.sql',
    checks: [
      {
        title: 'Automatic check: PHI Masking Guaranteed',
        subtitle: 'Encrypted columns & access logging',
        codePreview: 'CREATE TRIGGER log_phi_access AFTER SELECT OR UPDATE ON patients EXECUTE PROCEDURE record_audit();',
      },
      {
        title: 'Migration: Temporal Tables Active',
        subtitle: 'Point-in-time recovery compliance',
        codePreview: 'CREATE TABLE patients_history (LIKE patients INCLUDING ALL);',
      },
      {
        title: 'Namines Desk: HIPAA Portal',
        subtitle: 'Role-based access & anonymized viewer',
        codePreview: 'Desk.registerResource("patients", { table: "patients", maskedFields: ["ssn", "medical_notes"] });',
      },
    ],
  },
  {
    id: 'workflow',
    name: 'Workflow Engine',
    tables: 10,
    fks: 15,
    dialectRules: 'DAG Cycle Prevention + State Enums',
    safetyTier: 'Deterministic Safe',
    migrationFile: '20261018_workflow_dag_executions.sql',
    checks: [
      {
        title: 'Automatic check: Cycle Detection',
        subtitle: 'Acyclic constraint & enum valid',
        codePreview: 'CHECK (state IN (\'pending\', \'running\', \'completed\', \'failed\', \'cancelled\'))',
      },
      {
        title: 'Migration: Dead-Letter Queue Partition',
        subtitle: 'Partitioned execution queue generated',
        codePreview: 'CREATE TABLE pipeline_jobs PARTITION BY LIST (priority);',
      },
      {
        title: 'Namines Desk: Pipeline Runner',
        subtitle: 'Execution trace viewer & manual retry trigger',
        codePreview: 'Desk.registerAction("retryExecution", { handler: "/api/workflow/retry" });',
      },
    ],
  },
];

const VERIFY_TIMES: Record<string, string> = {
  ecommerce: '0.04s',
  saas: '0.03s',
  fintech: '0.05s',
  ai: '0.02s',
  healthcare: '0.06s',
  workflow: '0.03s',
};

const ENGINES = [
  'PostgreSQL 17',
  'MySQL 8.4',
  'SQLite 3',
  'SQL Server 2022',
  'Oracle DB 23ai',
  'MariaDB 10.5',
] as const;

export default function ClickClickDone() {
  const [selectedBlueprint, setSelectedBlueprint] = useState<BlueprintOption>(BLUEPRINT_OPTIONS[0]!);
  const [selectedEngineIndex, setSelectedEngineIndex] = useState(0);
  const [isVerifying, setIsVerifying] = useState(false);
  const [lastVerifiedTime, setLastVerifiedTime] = useState('0.04s');
  const [copiedPush, setCopiedPush] = useState(false);
  const [activeStep3Tab, setActiveStep3Tab] = useState<number | null>(0);
  const [mobileActiveStep, setMobileActiveStep] = useState<1 | 2 | 3>(1);

  const handleSelectBlueprint = (b: BlueprintOption) => {
    setSelectedBlueprint(b);
    setLastVerifiedTime(VERIFY_TIMES[b.id] ?? '0.03s');
  };

  const handleRunVerification = () => {
    setIsVerifying(true);
    setTimeout(() => {
      setIsVerifying(false);
      setLastVerifiedTime(VERIFY_TIMES[selectedBlueprint.id] ?? '0.03s');
    }, 450);
  };

  const handleCycleEngine = () => {
    setSelectedEngineIndex((prev) => (prev + 1) % ENGINES.length);
  };

  const handleCopyPush = () => {
    navigator.clipboard.writeText(`npx namines push --blueprint=${selectedBlueprint.id}`);
    setCopiedPush(true);
    setTimeout(() => setCopiedPush(false), 2000);
  };

  const currentEngine = ENGINES[selectedEngineIndex];

  return (
    <section id="how-it-works" className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-16 sm:py-24">
      {/* Section Header */}
      <div className="mb-10 sm:mb-14">
        <h2 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight text-content-primary">
          Design, verify, deploy.
        </h2>
        <p className="mt-3 text-sm sm:text-base text-content-muted max-w-2xl leading-relaxed">
          From an interactive blueprint or prompt to proven zero-drift database lifecycle in three seamless steps.
        </p>
      </div>

      {/* Mobile & Tablet Stepper Bar (Only 1 Step Shown at a Time, Zero Box Clutter) */}
      <div className="lg:hidden flex items-center justify-between p-1 bg-surface-850 rounded-[var(--radius-control)] border border-surface-600/70 mb-6 gap-1">
        <button
          type="button"
          onClick={() => setMobileActiveStep(1)}
          className={`flex-1 py-2 text-center text-xs font-mono font-bold rounded-[var(--radius-control)] transition-all cursor-pointer ${
            mobileActiveStep === 1
              ? 'bg-accent text-accent-on shadow-sm'
              : 'text-content-muted hover:text-content-primary'
          }`}
        >
          1. Blueprint
        </button>
        <button
          type="button"
          onClick={() => setMobileActiveStep(2)}
          className={`flex-1 py-2 text-center text-xs font-mono font-bold rounded-[var(--radius-control)] transition-all cursor-pointer ${
            mobileActiveStep === 2
              ? 'bg-accent text-accent-on shadow-sm'
              : 'text-content-muted hover:text-content-primary'
          }`}
        >
          2. Verify
        </button>
        <button
          type="button"
          onClick={() => setMobileActiveStep(3)}
          className={`flex-1 py-2 text-center text-xs font-mono font-bold rounded-[var(--radius-control)] transition-all cursor-pointer ${
            mobileActiveStep === 3
              ? 'bg-accent text-accent-on shadow-sm'
              : 'text-content-muted hover:text-content-primary'
          }`}
        >
          3. Deploy
        </button>
      </div>

      {/* 3 Columns Grid — On Desktop 3 Columns Side-by-Side, On Mobile/Tablet Single Focused Card */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8 lg:gap-6 items-stretch">
        {/* Step 1: Select or Prompt a Blueprint */}
        <div className={`flex-col h-full justify-between ${mobileActiveStep === 1 ? 'flex' : 'hidden lg:flex'}`}>
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

          {/* Equal Height Card */}
          <div className="min-h-[420px] h-full rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-3.5 flex flex-col justify-between shadow-xl">
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
                    onClick={() => handleSelectBlueprint(b)}
                    className={`w-full flex items-center justify-between px-3 py-2 rounded-[var(--radius-control)] text-xs font-mono transition-all cursor-pointer ${
                      isSelected
                        ? 'bg-accent text-accent-on font-bold shadow-md'
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

            {/* Footer Status & Mobile Advance */}
            <div className="pt-2 border-t border-surface-600/60 flex flex-col gap-2">
              <div className="flex items-center justify-between text-[11px] font-mono text-content-subtle">
                <span>Active: {selectedBlueprint.name}</span>
                <span className="text-accent-text font-bold">{selectedBlueprint.fks} relations</span>
              </div>
              <button
                type="button"
                onClick={() => setMobileActiveStep(2)}
                className="lg:hidden w-full py-2 px-3 rounded-[var(--radius-control)] bg-accent/15 hover:bg-accent/25 text-accent-text font-mono text-xs font-bold transition-all cursor-pointer text-center"
              >
                Proceed to Step 2: Verification &rarr;
              </button>
            </div>
          </div>
        </div>

        {/* Step 2: Verify Architecture with Deterministic Rules */}
        <div className={`flex-col h-full justify-between ${mobileActiveStep === 2 ? 'flex' : 'hidden lg:flex'}`}>
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
          <div className="min-h-[420px] h-full rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-3.5 flex flex-col justify-between shadow-xl">
            <div className="space-y-2">
              {/* Interactive Target Engine Row */}
              <button
                type="button"
                onClick={handleCycleEngine}
                title="Click to cycle target engine (6 supported)"
                className="w-full flex items-center justify-between text-xs font-mono p-2.5 bg-surface-800/80 hover:bg-surface-700/80 rounded-[var(--radius-control)] border border-surface-500/60 transition-colors cursor-pointer group"
              >
                <span className="text-content-muted group-hover:text-content-primary">Target Engine (Click to switch)</span>
                <div className="flex items-center gap-1.5 font-bold text-accent-text">
                  <span>{currentEngine}</span>
                  <RefreshCw className="w-3 h-3 opacity-60 group-hover:opacity-100 group-hover:rotate-180 transition-transform" />
                </div>
              </button>

              {/* Dialect Rules Row (Dynamic per blueprint) */}
              <div className="flex items-center justify-between text-xs font-mono p-2.5 bg-surface-800/80 rounded-[var(--radius-control)] border border-surface-500/60">
                <span className="text-content-muted">Dialect Rules</span>
                <span className="text-content-primary font-semibold truncate max-w-[200px]" title={selectedBlueprint.dialectRules}>
                  {selectedBlueprint.dialectRules}
                </span>
              </div>

              {/* Rule Engine Safety Tier (Dynamic per blueprint) */}
              <div className="flex items-center justify-between text-xs font-mono p-2.5 bg-surface-800/80 rounded-[var(--radius-control)] border border-surface-500/60">
                <span className="text-content-muted">Rule Engine</span>
                <span className="text-success-text font-semibold flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3" />
                  {selectedBlueprint.safetyTier}
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
                    <span>Analyzing {selectedBlueprint.tables} tables against {currentEngine}...</span>
                  </>
                ) : (
                  <>
                    <Zap className="w-3.5 h-3.5 text-accent-text" />
                    <span>Run Verification ({currentEngine})</span>
                  </>
                )}
              </button>
            </div>

            {/* Terminal Diagnostic Log Output */}
            <div className="p-3 bg-surface-800 rounded-[var(--radius-control)] border border-surface-500/80 font-mono text-[11px] flex flex-col gap-1">
              <div className="flex items-center justify-between text-success-text">
                <div className="flex items-center gap-1.5">
                  <CheckCircle2 className="w-3.5 h-3.5 text-success-text" />
                  <span className="font-bold">Verified in {lastVerifiedTime}</span>
                </div>
                <span className="text-[10px] text-content-subtle">24 AST rules</span>
              </div>
              <div className="text-content-muted text-[10px]">
                &rarr; Checked {selectedBlueprint.tables} tables &amp; {selectedBlueprint.fks} foreign keys for {selectedBlueprint.name}
              </div>
              <div className="text-content-secondary font-medium text-[10px] truncate">
                &rarr; Certified zero-drift for {currentEngine}
              </div>
            </div>

            {/* Mobile Advance to Step 3 */}
            <div className="lg:hidden pt-2 border-t border-surface-600/60 flex items-center gap-2">
              <button
                type="button"
                onClick={() => setMobileActiveStep(1)}
                className="w-1/3 py-2 rounded-[var(--radius-control)] bg-surface-800 text-content-muted font-mono text-xs transition-colors cursor-pointer text-center"
              >
                &larr; Step 1
              </button>
              <button
                type="button"
                onClick={() => setMobileActiveStep(3)}
                className="w-2/3 py-2 rounded-[var(--radius-control)] bg-accent/15 hover:bg-accent/25 text-accent-text font-mono text-xs font-bold transition-colors cursor-pointer text-center"
              >
                Step 3: Deploy &rarr;
              </button>
            </div>
          </div>
        </div>

        {/* Step 3: Deploy with Zero Drift */}
        <div className={`flex-col h-full justify-between ${mobileActiveStep === 3 ? 'flex' : 'hidden lg:flex'}`}>
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
          <div className="min-h-[420px] h-full rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-3.5 flex flex-col justify-between shadow-xl">
            {/* Clickable Terminal Push Header */}
            <button
              type="button"
              onClick={handleCopyPush}
              title="Click to copy push command"
              className="w-full p-2.5 bg-surface-800 hover:bg-surface-700 rounded-[var(--radius-control)] border border-surface-500/80 font-mono text-xs text-content-primary flex items-center justify-between transition-colors cursor-pointer group"
            >
              <div className="flex items-center gap-2 truncate">
                <Terminal className="w-3.5 h-3.5 text-accent-text shrink-0" />
                <span className="text-content-subtle">~/schema $</span>
                <span className="font-bold text-content-primary truncate">namines push --blueprint={selectedBlueprint.id}</span>
              </div>
              <div className="flex items-center gap-1 text-[11px] text-content-muted group-hover:text-accent-text shrink-0 ml-2">
                {copiedPush ? (
                  <>
                    <Check className="w-3 h-3 text-success-text" />
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

            {/* Clickable Status Cards List with Interactive Code Expansion */}
            <div className="space-y-2">
              {selectedBlueprint.checks.map((check, cIdx) => {
                const isActive = activeStep3Tab === cIdx;
                return (
                  <div
                    key={check.title}
                    className={`rounded-[var(--radius-control)] border transition-all overflow-hidden ${
                      isActive
                        ? 'bg-surface-750/90 border-accent/60 shadow-md'
                        : 'bg-surface-800/60 hover:bg-surface-800 border-surface-500/40'
                    }`}
                  >
                    <button
                      type="button"
                      onClick={() => setActiveStep3Tab(isActive ? null : cIdx)}
                      className="w-full text-left flex items-start gap-2.5 p-2.5 cursor-pointer"
                    >
                      <CheckCircle2 className="w-4 h-4 text-success-text shrink-0 mt-0.5" />
                      <div className="flex flex-col min-w-0 flex-1">
                        <span className="text-xs font-mono font-semibold text-content-primary truncate">
                          {check.title}
                        </span>
                        <span className="text-[10px] font-mono text-content-subtle truncate">
                          {check.subtitle}
                        </span>
                      </div>
                    </button>
                    {isActive && (
                      <div className="px-3 pb-2.5 pt-1 border-t border-surface-600/60 bg-surface-900/80 font-mono text-[10px] text-accent-text break-all">
                        <code>{check.codePreview}</code>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>

            {/* Step 3 Link to Demo */}
            <div className="pt-2 border-t border-surface-600/60 flex items-center justify-between text-[11px] font-mono text-content-subtle">
              <span className="truncate mr-2">File: {selectedBlueprint.migrationFile}</span>
              <Link href="/demo" className="text-accent-text hover:underline flex items-center gap-1 font-bold shrink-0">
                <Layers className="w-3 h-3" />
                <span>Open Blueprints</span>
              </Link>
            </div>

            {/* Mobile Step 3 Navigation Controls */}
            <div className="lg:hidden pt-2 border-t border-surface-600/60 flex items-center gap-2">
              <button
                type="button"
                onClick={() => setMobileActiveStep(1)}
                className="w-1/2 py-2 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 text-content-muted hover:text-content-primary font-mono text-xs transition-colors cursor-pointer text-center"
              >
                &larr; Step 1: Blueprint
              </button>
              <button
                type="button"
                onClick={() => setMobileActiveStep(2)}
                className="w-1/2 py-2 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 text-content-muted hover:text-content-primary font-mono text-xs transition-colors cursor-pointer text-center"
              >
                &larr; Step 2: Verify
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
