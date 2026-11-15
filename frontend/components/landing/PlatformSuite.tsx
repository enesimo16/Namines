'use client';

import Link from 'next/link';
import {
  ArrowRight,
  Database,
  GitPullRequest,
  FileCode2,
  Table2,
  Shield,
} from 'lucide-react';

interface SuiteModule {
  id: string;
  tag: string;
  title: string;
  description: string;
  href: string;
  cta: string;
  icon: typeof Database;
}

const SUITE_MODULES: SuiteModule[] = [
  {
    id: 'studio',
    tag: 'DESIGN',
    title: 'Schema Studio',
    description:
      'Turn plain text or ER diagrams into canonical 3NF tables with strict foreign keys and indexes.',
    href: '/new',
    cta: 'Open Studio',
    icon: Database,
  },
  {
    id: 'review',
    tag: 'GOVERNANCE',
    title: 'Change Review',
    description:
      'Classify migration risk automatically and validate DDL against ephemeral Docker containers.',
    href: '/review',
    cta: 'Inspect Review',
    icon: GitPullRequest,
  },
  {
    id: 'compile',
    tag: 'COMPILE & EJECT',
    title: 'Multi-Engine DDL',
    description:
      'Export deterministic SQL for six dialects alongside Prisma, Drizzle, EF Core, and TypeScript SDK.',
    href: '/compile',
    cta: 'View Targets',
    icon: FileCode2,
  },
  {
    id: 'desk',
    tag: 'DATA PLANE',
    title: 'Gateway & Desk',
    description:
      'Instant REST endpoints with scoped API keys, PII masking, and a live browser CRUD workspace.',
    href: '/apps/desk',
    cta: 'Launch Desk',
    icon: Table2,
  },
  {
    id: 'vault',
    tag: 'OPERATIONS',
    title: 'Vault, Ground & Flow',
    description:
      'AES-256-GCM encrypted backups, query telemetry, FinOps budgets, and event-driven webhooks.',
    href: '/apps/vault',
    cta: 'Explore Ops',
    icon: Shield,
  },
];

export default function PlatformSuite() {
  return (
    <section className="w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 py-16 lg:py-24">
      <div className="flex flex-col lg:flex-row lg:items-end justify-between gap-6 mb-10 lg:mb-12">
        <div className="max-w-2xl">
          <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text block mb-3">
            The Namines Platform
          </span>
          <h2 className="font-mono text-2xl sm:text-3xl lg:text-4xl font-semibold tracking-tight text-content-primary leading-tight">
            One lifecycle console from schema design to production ops
          </h2>
          <p className="font-sans text-sm sm:text-base text-content-secondary leading-relaxed mt-3">
            Every module reads from the same canonical intermediate representation—eliminating drift between your ERD, migrations, ORM types, and live admin panel.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3 shrink-0">
          <Link
            href="/new"
            className="inline-flex items-center justify-center gap-2 px-5 py-2.5 min-h-[44px] rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-sans text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
          >
            <span>Start designing</span>
            <ArrowRight className="w-4 h-4" />
          </Link>
          <Link
            href="/demo"
            className="inline-flex items-center justify-center gap-2 px-5 py-2.5 min-h-[44px] rounded-[var(--radius-control)] border border-surface-500 bg-surface-850 hover:bg-surface-800 hover:border-surface-400 text-content-primary font-sans text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
          >
            <span>Browse blueprints</span>
          </Link>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
        {SUITE_MODULES.map((mod) => {
          const Icon = mod.icon;
          return (
            <Link
              key={mod.id}
              href={mod.href}
              className="group flex flex-col justify-between p-5 lg:min-h-[300px] rounded-[var(--radius-card)] border border-surface-500 bg-surface-850/90 hover:bg-surface-800/80 hover:border-surface-400 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
            >
              <div>
                <div className="flex items-center justify-between mb-6">
                  <div className="w-9 h-9 rounded-[var(--radius-control)] border border-surface-500 bg-surface-900 flex items-center justify-center text-accent-text">
                    <Icon className="w-4 h-4" />
                  </div>
                  <span className="font-mono text-xs text-content-subtle tracking-wider">
                    {mod.tag}
                  </span>
                </div>

                <h3 className="font-mono text-base font-semibold text-content-primary mb-2">
                  {mod.title}
                </h3>
                <p className="font-sans text-sm text-content-muted leading-relaxed">
                  {mod.description}
                </p>
              </div>

              <div className="mt-6 pt-4 border-t border-surface-500/60 flex items-center justify-between font-mono text-xs text-accent-text group-hover:text-content-primary transition-colors">
                <span>{mod.cta}</span>
                <ArrowRight className="w-3.5 h-3.5 transition-transform group-hover:translate-x-0.5" />
              </div>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
