'use client';

import { useState } from 'react';
import Link from 'next/link';
import {
  ArrowRight,
  Check,
  CheckCircle2,
  Copy,
  FileCode2,
  GitPullRequest,
  Play,
  Terminal,
} from 'lucide-react';

type ShowcaseTabId = 'architect' | 'diff' | 'sandbox' | 'dialects';
type DialectKey = 'PostgreSQL' | 'SQL Server' | 'MySQL' | 'SQLite';

interface ShowcaseTab {
  id: ShowcaseTabId;
  label: string;
  icon: typeof FileCode2;
  eyebrow: string;
  title: string;
  description: string;
  highlights: string[];
  ctaLabel: string;
  ctaHref: string;
  filename: string;
  code: string;
}

const DIALECT_CODE: Record<DialectKey, string> = {
  PostgreSQL: `-- PostgreSQL 17 (Native DDL + Concurrent Index)
CREATE TABLE IF NOT EXISTS "public"."orders" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "user_id" uuid NOT NULL REFERENCES "public"."users"("id") ON DELETE RESTRICT,
  "amount" numeric(12, 2) NOT NULL CHECK ("amount" >= 0),
  "metadata" jsonb NOT NULL DEFAULT '{}'::jsonb,
  "created_at" timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS "idx_orders_user_created"
  ON "public"."orders" ("user_id", "created_at" DESC);`,
  'SQL Server': `-- SQL Server 2022 (T-SQL)
CREATE TABLE [dbo].[Orders] (
  [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  [UserId] UNIQUEIDENTIFIER NOT NULL,
  [Amount] DECIMAL(12, 2) NOT NULL CHECK ([Amount] >= 0),
  [Metadata] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
  [CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT [FK_Orders_Users] FOREIGN KEY ([UserId])
    REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
);`,
  MySQL: `-- MySQL 8.4 LTS (InnoDB)
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
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC;`,
  SQLite: `-- SQLite 3 (STRICT Mode)
CREATE TABLE IF NOT EXISTS "orders" (
  "id" TEXT PRIMARY KEY NOT NULL,
  "user_id" TEXT NOT NULL REFERENCES "users"("id") ON DELETE RESTRICT,
  "amount" REAL NOT NULL CHECK ("amount" >= 0),
  "metadata" TEXT NOT NULL DEFAULT '{}',
  "created_at" TEXT NOT NULL DEFAULT (datetime('now'))
) STRICT;`,
};

const TABS: ShowcaseTab[] = [
  {
    id: 'architect',
    label: '1. Schema Architect',
    icon: FileCode2,
    eyebrow: 'CANONICAL IR',
    title: 'Deterministic schemas from plain intent',
    description:
      'Every prompt or imported database compiles into a single engine-agnostic IR before touching SQL. Foreign keys, check constraints, and indexes are validated structurally.',
    highlights: [
      'Automatic 3NF normalization & orphaned FK checks',
      'No implicit ON DELETE CASCADE defaults—restrictive by policy',
      'Versioned schema.ir.json ready for Git review',
    ],
    ctaLabel: 'Open Schema Studio',
    ctaHref: '/new',
    filename: 'schema.ir.json',
    code: `{
  "name": "ecommerce_core",
  "version": "1.4.0",
  "tables": [
    {
      "name": "orders",
      "columns": [
        { "name": "id", "type": "uuid", "primaryKey": true },
        { "name": "user_id", "type": "uuid", "nullable": false },
        { "name": "amount", "type": "decimal(12,2)", "check": "amount >= 0" }
      ],
      "foreignKeys": [
        { "column": "user_id", "references": "users.id", "onDelete": "RESTRICT" }
      ]
    }
  ]
}`,
  },
  {
    id: 'diff',
    label: '2. PR Schema Diff',
    icon: GitPullRequest,
    eyebrow: 'ZERO DRIFT',
    title: 'Risk-classified migrations on every pull request',
    description:
      'Namines compares your branch IR against the target baseline and classifies every DDL statement into Safe, Warning, or Destructive before anything merges.',
    highlights: [
      'Blocks unreviewed DROP COLUMN or type-narrowing edits',
      'Pinpoints affected API routes, ORM models, and queries',
      'Immutable ChangeRequest audit log with signed approvals',
    ],
    ctaLabel: 'Inspect Change Review',
    ctaHref: '/review',
    filename: 'PR 142 · feature/orders-ledger',
    code: `MIGRATION IMPACT REPORT · 20261115_orders_ledger.sql
────────────────────────────────────────────────────────
[SAFE]        ADD TABLE public.ledger_entries (0 locks)
[SAFE]        CREATE INDEX CONCURRENTLY idx_ledger_order
[WARNING]     ALTER TABLE orders ADD COLUMN currency char(3)
              ↳ Non-null column requires explicit DEFAULT
[BLOCKED]     DROP COLUMN orders.legacy_tax_code
              ↳ Requires Owner approval + AuditLog signature

Affected symbols: 3 · Gateway routes: /api/v1/orders`,
  },
  {
    id: 'sandbox',
    label: '3. Sandbox Run Tests',
    icon: Play,
    eyebrow: 'EPHEMERAL VERIFICATION',
    title: 'Prove every migration against a real engine container',
    description:
      'Static linters miss dialect edge cases. Spin up an isolated container, apply your full migration chain, and verify constraints before touching staging or production.',
    highlights: [
      'Real PostgreSQL, MySQL, SQL Server, and SQLite execution',
      'Detects circular cascades, reserved words, and lock timeouts',
      'Ephemeral container torn down immediately after verification',
    ],
    ctaLabel: 'Explore Run Tests',
    ctaHref: '/review',
    filename: 'namines test --engine postgres:17',
    code: `$ namines test --engine postgres:17 --migration 20261115_orders

[00.4s] Provisioned ephemeral postgres:17-alpine container
[01.1s] Applied baseline schema (14 tables, 22 foreign keys)
[01.6s] Applied candidate migration 20261115_orders.sql
[01.8s] Verified CHECK constraints & FK restrict behavior
[02.0s] Container destroyed · Exit code 0

Result: PASSED (0 errors, 0 lock-timeout violations)`,
  },
  {
    id: 'dialects',
    label: '4. Multi-Engine DDL',
    icon: Terminal,
    eyebrow: 'SIX DIALECTS + 19 EJECTS',
    title: 'Compile one canonical model to any SQL dialect',
    description:
      'Switch target engines without rewriting migrations. Namines quotes identifiers safely, maps types accurately, and falls back to restrictive referential actions.',
    highlights: [
      'PostgreSQL, MySQL, SQL Server, Oracle, MariaDB, SQLite',
      'Exports Prisma, Drizzle, EF Core 8/9, and TypeScript SDK',
      'Golden-file verified SQL output across all 6 engines',
    ],
    ctaLabel: 'Open Compiler & Eject',
    ctaHref: '/compile',
    filename: 'orders.sql',
    code: DIALECT_CODE.PostgreSQL,
  },
];

export default function LifecycleShowcase() {
  const [activeTab, setActiveTab] = useState<ShowcaseTabId>('architect');
  const [activeDialect, setActiveDialect] = useState<DialectKey>('PostgreSQL');
  const [copied, setCopied] = useState(false);

  const current = TABS.find((t) => t.id === activeTab) ?? TABS[0];
  const displayedCode =
    activeTab === 'dialects' ? DIALECT_CODE[activeDialect] : current.code;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(displayedCode);
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      // Clipboard API blocked in non-secure context; ignore silently.
    }
  };

  return (
    <section
      id="spotlights"
      className="w-full max-w-[1144px] mx-auto px-4 sm:px-6 lg:px-8 py-16 lg:py-24"
    >
      <div id="zero-drift" className="text-center max-w-2xl mx-auto mb-8 lg:mb-10">
        <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text block mb-3">
          Lifecycle Workflow
        </span>
        <h2 className="font-mono text-2xl sm:text-3xl lg:text-4xl font-semibold tracking-tight text-content-primary leading-tight">
          Design, diff, and verify before a single row is touched
        </h2>
      </div>

      {/* Centered Tablist Pill Bar (DeepL Reference Pattern) */}
      <div className="flex justify-center mb-8">
        <div
          role="tablist"
          aria-label="Namines lifecycle stages"
          className="inline-flex flex-wrap justify-center gap-1.5 p-1.5 rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/90"
        >
          {TABS.map((tab) => {
            const Icon = tab.icon;
            const isActive = tab.id === activeTab;
            return (
              <button
                key={tab.id}
                role="tab"
                type="button"
                aria-selected={isActive}
                onClick={() => setActiveTab(tab.id)}
                className={`inline-flex items-center gap-2 px-4 py-2 min-h-[44px] rounded-[var(--radius-control)] font-mono text-xs font-medium transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring ${
                  isActive
                    ? 'bg-accent text-accent-on'
                    : 'text-content-muted hover:text-content-primary hover:bg-surface-800'
                }`}
              >
                <Icon className="w-3.5 h-3.5 shrink-0" />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </div>
      </div>

      {/* Main Showcase Panel */}
      <div
        role="tabpanel"
        className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/95 p-6 sm:p-8 lg:p-10"
      >
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-stretch">
          {/* Left Column: Explanation & Guarantees */}
          <div className="lg:col-span-5 flex flex-col justify-between gap-6">
            <div>
              <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text block mb-2.5">
                {current.eyebrow}
              </span>
              <h3 className="font-mono text-xl sm:text-2xl font-semibold text-content-primary leading-snug mb-3">
                {current.title}
              </h3>
              <p className="font-sans text-sm sm:text-base text-content-secondary leading-relaxed mb-6">
                {current.description}
              </p>

              <ul className="space-y-3">
                {current.highlights.map((item) => (
                  <li
                    key={item}
                    className="flex items-start gap-2.5 font-sans text-sm text-content-muted"
                  >
                    <CheckCircle2 className="w-4 h-4 text-accent-text shrink-0 mt-0.5" />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div className="pt-4 border-t border-surface-500/70">
              <Link
                href={current.ctaHref}
                className="inline-flex items-center gap-2 font-mono text-xs font-semibold text-accent-text hover:text-content-primary transition-colors min-h-[44px] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
              >
                <span>{current.ctaLabel}</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </div>
          </div>

          {/* Right Column: Code / Inspection Surface */}
          <div className="lg:col-span-7 flex flex-col rounded-[var(--radius-card)] border border-surface-500 bg-surface-850 overflow-hidden">
            <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 border-b border-surface-500 bg-surface-900/80">
              <span className="font-mono text-xs text-content-subtle truncate">
                {activeTab === 'dialects'
                  ? `${activeDialect.toLowerCase().replace(/\s+/g, '_')}_orders.sql`
                  : current.filename}
              </span>

              <div className="flex items-center gap-2">
                {activeTab === 'dialects' && (
                  <div className="flex items-center gap-1">
                    {(Object.keys(DIALECT_CODE) as DialectKey[]).map((dialect) => (
                      <button
                        key={dialect}
                        type="button"
                        onClick={() => setActiveDialect(dialect)}
                        className={`tap-44 px-2.5 py-1 rounded-[var(--radius-control)] font-mono text-xs transition-colors cursor-pointer ${
                          activeDialect === dialect
                            ? 'bg-accent text-accent-on'
                            : 'text-content-muted hover:text-content-primary hover:bg-surface-800'
                        }`}
                      >
                        {dialect}
                      </button>
                    ))}
                  </div>
                )}

                <button
                  type="button"
                  onClick={handleCopy}
                  aria-label="Copy code snippet"
                  className="tap-44 inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] border border-surface-500 bg-surface-800 hover:border-surface-400 font-mono text-xs text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
                >
                  {copied ? (
                    <>
                      <Check className="w-3.5 h-3.5 text-success-text" />
                      <span>Copied</span>
                    </>
                  ) : (
                    <>
                      <Copy className="w-3.5 h-3.5" />
                      <span>Copy</span>
                    </>
                  )}
                </button>
              </div>
            </div>

            <pre className="p-4 sm:p-5 whitespace-pre-wrap break-words font-mono text-xs sm:text-sm leading-relaxed text-content-secondary flex-1">
              <code>{displayedCode}</code>
            </pre>
          </div>
        </div>
      </div>
    </section>
  );
}
