'use client';

import { useState } from 'react';
import Link from 'next/link';
import {
  ShieldAlert,
  CheckCircle2,
  AlertTriangle,
  ArrowRight,
  GitBranch,
  Layers,
  Database,
  Users2,
  Terminal,
} from 'lucide-react';

const ENGINE_SNIPPETS: Record<string, string> = {
  PostgreSQL: `-- PostgreSQL 17 Dialect (Native DDL)
CREATE TABLE IF NOT EXISTS "public"."orders" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "user_id" uuid NOT NULL REFERENCES "public"."users"("id") ON DELETE RESTRICT,
  "amount" numeric(12, 2) NOT NULL CHECK ("amount" >= 0),
  "metadata" jsonb NOT NULL DEFAULT '{}'::jsonb,
  "created_at" timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS "idx_orders_user_created"
  ON "public"."orders" ("user_id", "created_at" DESC);`,
  'SQL Server': `-- SQL Server 2022 Dialect
CREATE TABLE [dbo].[Orders] (
  [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  [UserId] UNIQUEIDENTIFIER NOT NULL,
  [Amount] DECIMAL(12, 2) NOT NULL CHECK ([Amount] >= 0),
  [Metadata] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
  [CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT [FK_Orders_Users] FOREIGN KEY ([UserId])
    REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
);

CREATE NONCLUSTERED INDEX [IX_Orders_User_Created]
  ON [dbo].[Orders] ([UserId], [CreatedAt] DESC);`,
  MySQL: `-- MySQL 8.4 LTS Dialect
CREATE TABLE IF NOT EXISTS \`orders\` (
  \`id\` CHAR(36) NOT NULL,
  \`user_id\` CHAR(36) NOT NULL,
  \`amount\` DECIMAL(12, 2) NOT NULL,
  \`metadata\` JSON NOT NULL,
  \`created_at\` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (\`id\`),
  CONSTRAINT \`chk_orders_amount\` CHECK (\`amount\` >= 0),
  CONSTRAINT \`fk_orders_user\` FOREIGN KEY (\`user_id\`)
    REFERENCES \`users\` (\`id\`) ON DELETE RESTRICT
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC;

CREATE INDEX \`idx_orders_user_created\`
  ON \`orders\` (\`user_id\`, \`created_at\` DESC);`,
  SQLite: `-- SQLite 3 Dialect (Strict Mode)
CREATE TABLE IF NOT EXISTS "orders" (
  "id" TEXT PRIMARY KEY,
  "user_id" TEXT NOT NULL,
  "amount" REAL NOT NULL CHECK ("amount" >= 0),
  "metadata" TEXT NOT NULL DEFAULT '{}',
  "created_at" TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  FOREIGN KEY ("user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
) STRICT;

CREATE INDEX IF NOT EXISTS "idx_orders_user_created"
  ON "orders" ("user_id", "created_at" DESC);`,
};

export default function ArchitectureSpotlights() {
  const [selectedEngine, setSelectedEngine] = useState<string>('PostgreSQL');

  return (
    <section className="relative z-10 w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-20 sm:py-28">
      {/* Section Header */}
      <div className="mb-16 sm:mb-20 text-center max-w-2xl mx-auto">
        <span className="font-mono text-xs font-bold tracking-widest uppercase text-accent-text block mb-3">
          Deep Architecture
        </span>
        <h2 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight text-content-primary mb-4">
          Engineered for production durability
        </h2>
        <p className="text-xs sm:text-sm text-content-muted leading-relaxed">
          From the visual canvas to containerized ephemeral DDL execution, every layer of Namines is built to prevent database disasters before code merges.
        </p>
      </div>

      {/* Spotlight 1: Rule Engine & Validation */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center mb-24">
        <div className="lg:col-span-5 flex flex-col gap-4">
          <h3 className="text-2xl sm:text-3xl font-bold text-content-primary tracking-tight">
            The AI proposes. 24 static rules decide.
          </h3>
          <p className="text-xs sm:text-sm text-content-muted leading-relaxed">
            Large language models hallucinate types, omit indexes on high-cardinality foreign keys, and drop cascade rules. Namines pairs generative design with a deterministic static analyzer that verifies integrity in sub-millisecond cycles.
          </p>
          <ul className="flex flex-col gap-2 mt-2 text-xs font-mono text-content-secondary">
            <li className="flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 text-success-text shrink-0" />
              <span>Zero orphaned foreign key definitions</span>
            </li>
            <li className="flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 text-success-text shrink-0" />
              <span>Automated indexing recommendation for query filters</span>
            </li>
            <li className="flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 text-success-text shrink-0" />
              <span>Cross-engine type compatibility guarantees</span>
            </li>
          </ul>
          <Link
            href="/demo"
            className="inline-flex items-center gap-1.5 text-xs font-mono text-accent-text hover:text-content-primary transition-colors mt-2"
          >
            <span>Read the rule engine specification</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>

        <div className="lg:col-span-7">
          <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/95 p-6 shadow-2xl overflow-hidden">
            <div className="flex items-center justify-between border-b border-surface-500/70 pb-4 mb-4 font-mono text-xs">
              <div className="flex items-center gap-2">
                <ShieldAlert className="w-4 h-4 text-accent-text" />
                <span className="font-bold text-content-primary">SchemaIntegrityAnalyzer</span>
              </div>
              <span className="text-[11px] text-content-subtle">AstCheck: 24 rules passed</span>
            </div>

            <div className="flex flex-col gap-3 font-mono text-xs">
              {/* Finding 1 */}
              <div className="rounded-[var(--radius-card)] border border-surface-500/80 bg-surface-800/80 p-3.5 flex items-start gap-3">
                <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold bg-success/20 text-success-text shrink-0 mt-0.5">
                  OPTIMAL
                </span>
                <div className="flex flex-col gap-1">
                  <span className="font-bold text-content-primary">Foreign Key Index Validated</span>
                  <span className="text-content-muted text-[11px]">
                    Table `orders.user_id` has matching composite index `idx_orders_user_created`.
                  </span>
                </div>
              </div>

              {/* Finding 2 */}
              <div className="rounded-[var(--radius-card)] border border-surface-500/80 bg-surface-800/80 p-3.5 flex items-start gap-3">
                <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold bg-accent/20 text-accent-text shrink-0 mt-0.5">
                  ENFORCED
                </span>
                <div className="flex flex-col gap-1">
                  <span className="font-bold text-content-primary">Referential Action Safe Fallback</span>
                  <span className="text-content-muted text-[11px]">
                    Engine policy: Default fallback set to `RESTRICT/NO ACTION`. Never falls back to `CASCADE`.
                  </span>
                </div>
              </div>

              {/* Finding 3 */}
              <div className="rounded-[var(--radius-card)] border border-surface-500/80 bg-surface-800/80 p-3.5 flex items-start gap-3">
                <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold bg-surface-700 text-content-secondary shrink-0 mt-0.5">
                  PASS
                </span>
                <div className="flex flex-col gap-1">
                  <span className="font-bold text-content-primary">Primary Key Strategy</span>
                  <span className="text-content-muted text-[11px]">
                    UUIDv7 / Sequential GUID configured for optimal B-Tree clustering on high write volume.
                  </span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Spotlight 2: Non-Destructive Migration Lifecycle */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center mb-24">
        <div className="lg:col-span-7 order-2 lg:order-1">
          <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/95 p-6 shadow-2xl">
            <div className="flex items-center justify-between border-b border-surface-500/70 pb-4 mb-5 font-mono text-xs">
              <div className="flex items-center gap-2">
                <GitBranch className="w-4 h-4 text-accent-text" />
                <span className="font-bold text-content-primary">Migration Risk Pipeline</span>
              </div>
            </div>

            {/* Visual Pipeline Stages */}
            <div className="grid grid-cols-3 gap-2 mb-6 font-mono text-xs">
              <div className="rounded-[var(--radius-control)] border border-surface-500 bg-surface-800 p-2.5 flex flex-col gap-1 text-center">
                <span className="text-[10px] text-content-subtle uppercase">Phase 1</span>
                <span className="font-bold text-content-primary">AST Diff</span>
                <span className="text-[10px] text-success-text">Passed</span>
              </div>
              <div className="rounded-[var(--radius-control)] border border-surface-500 bg-surface-800 p-2.5 flex flex-col gap-1 text-center">
                <span className="text-[10px] text-content-subtle uppercase">Phase 2</span>
                <span className="font-bold text-content-primary">Ephemeral Docker</span>
                <span className="text-[10px] text-success-text">Verified DDL</span>
              </div>
              <div className="rounded-[var(--radius-control)] border border-surface-500 bg-surface-800 p-2.5 flex flex-col gap-1 text-center">
                <span className="text-[10px] text-content-subtle uppercase">Phase 3</span>
                <span className="font-bold text-content-primary">Risk Sign-off</span>
                <span className="text-[10px] text-accent-text">Audit Recorded</span>
              </div>
            </div>

            {/* DDL Diff Preview */}
            <div className="rounded-[var(--radius-card)] border border-surface-500/80 bg-surface-800/80 p-4 font-mono text-xs flex flex-col gap-2">
              <div className="flex items-center justify-between text-[11px] text-content-subtle pb-2 border-b border-surface-500/50">
                <span>migrations/20260907_add_orders_currency.sql</span>
                <span className="text-success-text font-bold">+2 lines</span>
              </div>
              <div className="text-success-text leading-relaxed">
                + ALTER TABLE &quot;orders&quot; ADD COLUMN IF NOT EXISTS &quot;currency&quot; varchar(3) DEFAULT &apos;USD&apos;;
              </div>
              <div className="text-success-text leading-relaxed">
                + CREATE INDEX CONCURRENTLY IF NOT EXISTS &quot;idx_orders_currency&quot; ON &quot;orders&quot; (&quot;currency&quot;);
              </div>
              <div className="text-content-subtle text-[11px] pt-1">
                {'// Destructive operation "DROP COLUMN" blocked: Requires manual override & audit log'}
              </div>
            </div>
          </div>
        </div>

        <div className="lg:col-span-5 order-1 lg:order-2 flex flex-col gap-4">
          <h3 className="text-2xl sm:text-3xl font-bold text-content-primary tracking-tight">
            Non-destructive migration lifecycle
          </h3>
          <p className="text-xs sm:text-sm text-content-muted leading-relaxed">
            Never drop a table in production by accident. Namines classifies every change request into Safe, Warning, or Destructive. Safe operations auto-approve, while destructive actions require mandatory audit logging and rollback scripts.
          </p>
          <div className="flex flex-col gap-2 text-xs font-mono text-content-secondary mt-2">
            <div className="flex items-start gap-2">
              <AlertTriangle className="w-4 h-4 text-accent-text shrink-0 mt-0.5" />
              <span>Pre-execution dry run on ephemeral Docker containers</span>
            </div>
            <div className="flex items-start gap-2">
              <CheckCircle2 className="w-4 h-4 text-success-text shrink-0 mt-0.5" />
              <span>Zero-lock DDL: CONCURRENTLY and lock-timeout guards applied</span>
            </div>
          </div>
          <Link
            href="/demo"
            className="inline-flex items-center gap-1.5 text-xs font-mono text-accent-text hover:text-content-primary transition-colors mt-2"
          >
            <span>Explore ChangeRequest review flow</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>
      </div>

      {/* Spotlight 3: Six Real SQL Engines */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center mb-24">
        <div className="lg:col-span-5 flex flex-col gap-4">
          <h3 className="text-2xl sm:text-3xl font-bold text-content-primary tracking-tight">
            Native DDL for six battle-tested engines
          </h3>
          <p className="text-xs sm:text-sm text-content-muted leading-relaxed">
            No lowest-common-denominator compromise. Write your schema once in canonical JSON IR; Namines generates genuine, idiomatic DDL respecting quotes, collations, generated columns, and index mechanics for your target database.
          </p>
          <div className="flex flex-wrap gap-2 mt-2">
            {Object.keys(ENGINE_SNIPPETS).map((eng) => (
              <button
                key={eng}
                type="button"
                onClick={() => setSelectedEngine(eng)}
                className={`px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-mono transition-colors border ${
                  selectedEngine === eng
                    ? 'border-surface-400 bg-surface-700 text-content-primary font-bold'
                    : 'border-surface-500 bg-surface-800/60 text-content-secondary hover:text-content-primary hover:bg-surface-800'
                }`}
              >
                {eng}
              </button>
            ))}
          </div>
          <Link
            href="/demo"
            className="inline-flex items-center gap-1.5 text-xs font-mono text-accent-text hover:text-content-primary transition-colors mt-2"
          >
            <span>Compare engine DDL capabilities</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>

        <div className="lg:col-span-7">
          <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/95 p-6 shadow-2xl overflow-hidden">
            <div className="flex items-center justify-between border-b border-surface-500/70 pb-3 mb-4 font-mono text-xs">
              <div className="flex items-center gap-2">
                <Terminal className="w-4 h-4 text-accent-text" />
                <span className="font-bold text-content-primary">output.{selectedEngine.toLowerCase().replace(' ', '')}.sql</span>
              </div>
              <span className="text-[11px] text-content-subtle">Dialect-verified</span>
            </div>
            <pre className="font-mono text-xs leading-relaxed text-content-secondary overflow-x-auto p-4 rounded-[var(--radius-card)] bg-surface-800/80 border border-surface-500/70 max-h-[300px]">
              <code>{ENGINE_SNIPPETS[selectedEngine]}</code>
            </pre>
          </div>
        </div>
      </div>

      {/* Spotlight 4: Real-time Collaboration & Drift Detection */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center">
        <div className="lg:col-span-7 order-2 lg:order-1">
          <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/95 p-6 shadow-2xl">
            <div className="flex items-center justify-between border-b border-surface-500/70 pb-4 mb-5 font-mono text-xs">
              <div className="flex items-center gap-2">
                <Users2 className="w-4 h-4 text-accent-text" />
                <span className="font-bold text-content-primary">Multiplayer Canvas Room</span>
              </div>
            </div>

            {/* Collaborators strip */}
            <div className="flex items-center justify-between p-3 rounded-[var(--radius-control)] bg-surface-800/80 border border-surface-500/70 mb-5 text-xs font-mono">
              <div className="flex items-center gap-3">
                <div className="flex -space-x-2">
                  <span className="w-7 h-7 rounded-full bg-accent/30 border border-surface-500 flex items-center justify-center font-bold text-accent-text text-[10px]">
                    EY
                  </span>
                  <span className="w-7 h-7 rounded-full bg-surface-600 border border-surface-500 flex items-center justify-center font-bold text-content-primary text-[10px]">
                    AK
                  </span>
                  <span className="w-7 h-7 rounded-full bg-surface-700 border border-surface-500 flex items-center justify-center font-bold text-content-secondary text-[10px]">
                    NB
                  </span>
                </div>
                <span className="text-content-secondary">3 active architects</span>
              </div>
              <span className="text-content-subtle text-[11px]">Branch: feature/order-schema</span>
            </div>

            {/* Drift Status Banner */}
            <div className="rounded-[var(--radius-card)] border border-surface-500/80 bg-surface-800/50 p-4 font-mono text-xs flex flex-col gap-2">
              <div className="flex items-center justify-between text-[11px]">
                <span className="text-content-muted">Production Catalog AST Sync:</span>
                <span className="text-success-text font-bold">0 Drift Detected</span>
              </div>
              <div className="w-full bg-surface-700 h-1.5 rounded-full overflow-hidden">
                <div className="bg-success h-full w-full" />
              </div>
              <span className="text-[11px] text-content-subtle mt-1">
                Database schema matches code representation perfectly. No out-of-band table alterations.
              </span>
            </div>
          </div>
        </div>

        <div className="lg:col-span-5 order-1 lg:order-2 flex flex-col gap-4">
          <h3 className="text-2xl sm:text-3xl font-bold text-content-primary tracking-tight">
            Real-time multiplayer canvas &amp; drift monitor
          </h3>
          <p className="text-xs sm:text-sm text-content-muted leading-relaxed">
            Stop reviewing database designs in fragmented screenshots. Design collaboratively on an infinite canvas with live presence, shared branches, and automated AST drift detection that watches your production database.
          </p>
          <div className="flex flex-col gap-2 text-xs font-mono text-content-secondary mt-2">
            <div className="flex items-center gap-2">
              <Layers className="w-4 h-4 text-accent-text shrink-0" />
              <span>Multiplayer cursor presence backed by SignalR</span>
            </div>
            <div className="flex items-center gap-2">
              <Database className="w-4 h-4 text-success-text shrink-0" />
              <span>Continuous catalog introspection detects out-of-band drift</span>
            </div>
          </div>
          <Link
            href="/demo"
            className="inline-flex items-center gap-1.5 text-xs font-mono text-accent-text hover:text-content-primary transition-colors mt-2"
          >
            <span>Launch interactive canvas</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>
      </div>
    </section>
  );
}
